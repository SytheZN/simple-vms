using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;
using Cameras.Onvif.Soap;

namespace Cameras.Onvif.Services;

public static class WsDiscovery
{
  private static readonly IPAddress MulticastAddress = IPAddress.Parse("239.255.255.250");
  private const int MulticastPort = 3702;
  private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);
  private static readonly int[] CommonOnvifPorts = [80, 8080, 8899];
  private const int MaxParallelScans = 64;

  public static async Task<IReadOnlyList<string>> ProbeAsync(CancellationToken ct)
  {
    var messageId = $"urn:uuid:{Guid.NewGuid()}";
    var probe = BuildProbeMessage(messageId);
    var probeBytes = Encoding.UTF8.GetBytes(probe.ToString());
    var endpoint = new IPEndPoint(MulticastAddress, MulticastPort);

    using var udp = new UdpClient();
    udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
    udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
    udp.JoinMulticastGroup(MulticastAddress);

    await udp.SendAsync(probeBytes, endpoint, ct);

    var addresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    using var timeout = new CancellationTokenSource(ProbeTimeout);
    using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

    try
    {
      while (!linked.Token.IsCancellationRequested)
      {
        var result = await udp.ReceiveAsync(linked.Token);
        var xml = Encoding.UTF8.GetString(result.Buffer);
        foreach (var addr in ParseProbeMatch(xml))
          addresses.Add(addr);
      }
    }
    catch (OperationCanceledException) when (timeout.IsCancellationRequested)
    {
    }

    return [.. addresses];
  }

  public static async Task<IReadOnlyList<string>> ScanSubnetsAsync(
    HttpClient http, string[] subnets, CancellationToken ct)
  {
    var addresses = new List<string>();
    using var semaphore = new SemaphoreSlim(MaxParallelScans);

    var tasks = new List<Task<string?>>();
    foreach (var subnet in subnets)
    {
      foreach (var ip in EnumerateSubnet(subnet))
      {
        foreach (var port in CommonOnvifPorts)
        {
          tasks.Add(ProbeHostAsync(http, semaphore, ip, port, ct));
        }
      }
    }

    var results = await Task.WhenAll(tasks);
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var addr in results)
    {
      if (addr != null && seen.Add(addr))
        addresses.Add(addr);
    }

    return addresses;
  }

  private static async Task<string?> ProbeHostAsync(
    HttpClient http, SemaphoreSlim semaphore, IPAddress ip, int port, CancellationToken ct)
  {
    await semaphore.WaitAsync(ct);
    try
    {
      var uri = $"http://{ip}:{port}/onvif/device_service";
      var body = new XElement(XmlHelpers.NsDevice + "GetSystemDateAndTime");
      var envelope = XmlHelpers.BuildEnvelope(body);

      using var content = new StringContent(
        envelope.ToString(), Encoding.UTF8, "application/soap+xml");
      using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
      cts.CancelAfter(TimeSpan.FromSeconds(2));

      using var response = await http.PostAsync(uri, content, cts.Token);
      if (response.IsSuccessStatusCode)
        return uri;
    }
    catch
    {
    }
    finally
    {
      semaphore.Release();
    }
    return null;
  }

  internal static IEnumerable<IPAddress> EnumerateSubnet(string cidr)
  {
    var parts = cidr.Split('/');
    if (!IPAddress.TryParse(parts[0], out var network))
      yield break;

    var prefixLen = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 32;
    if (prefixLen < 1 || prefixLen > 32) yield break;

    var networkBytes = network.GetAddressBytes();
    var networkUint = (uint)networkBytes[0] << 24
      | (uint)networkBytes[1] << 16
      | (uint)networkBytes[2] << 8
      | networkBytes[3];

    var hostBits = 32 - prefixLen;
    var hostCount = 1u << hostBits;
    var baseAddr = networkUint & (uint.MaxValue << hostBits);

    var start = hostCount > 2 ? 1u : 0u;
    var end = hostCount > 2 ? hostCount - 1 : hostCount;

    for (var i = start; i < end; i++)
    {
      var addr = baseAddr + i;
      yield return new IPAddress(new[]
      {
        (byte)(addr >> 24),
        (byte)(addr >> 16),
        (byte)(addr >> 8),
        (byte)addr
      });
    }
  }

  public static XDocument BuildProbeMessage(string messageId)
  {
    return new XDocument(
      new XElement(XmlHelpers.NsSoap + "Envelope",
        new XAttribute(XNamespace.Xmlns + "s", XmlHelpers.NsSoap),
        new XAttribute(XNamespace.Xmlns + "a", XmlHelpers.NsWsa),
        new XAttribute(XNamespace.Xmlns + "d", XmlHelpers.NsWsd),
        new XAttribute(XNamespace.Xmlns + "dn", XmlHelpers.NsDn),
        new XElement(XmlHelpers.NsSoap + "Header",
          new XElement(XmlHelpers.NsWsa + "MessageID", messageId),
          new XElement(XmlHelpers.NsWsa + "To", "urn:schemas-xmlsoap-org:ws:2005:04:discovery"),
          new XElement(XmlHelpers.NsWsa + "Action",
            "http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe")),
        new XElement(XmlHelpers.NsSoap + "Body",
          new XElement(XmlHelpers.NsWsd + "Probe",
            new XElement(XmlHelpers.NsWsd + "Types", "dn:NetworkVideoTransmitter")))));
  }

  public static IEnumerable<string> ParseProbeMatch(string xml)
  {
    XDocument doc;
    try { doc = XDocument.Parse(xml); }
    catch { yield break; }

    var body = XmlHelpers.GetBody(doc);
    if (body == null) yield break;

    var matches = body.Element(XmlHelpers.NsWsd + "ProbeMatches");
    if (matches == null) yield break;

    foreach (var match in matches.Elements(XmlHelpers.NsWsd + "ProbeMatch"))
    {
      var xaddrs = match.Element(XmlHelpers.NsWsd + "XAddrs")?.Value;
      if (string.IsNullOrWhiteSpace(xaddrs)) continue;

      foreach (var addr in xaddrs.Split(' ', StringSplitOptions.RemoveEmptyEntries))
      {
        if (Uri.TryCreate(addr, UriKind.Absolute, out var uri))
          yield return NormalizeDeviceUri(uri);
      }
    }
  }

  private static string NormalizeDeviceUri(Uri uri) =>
    $"http://{uri.Host}:{uri.Port}/onvif/device_service";
}
