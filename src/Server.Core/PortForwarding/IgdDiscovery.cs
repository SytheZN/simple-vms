using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;

namespace Server.Core.PortForwarding;

public sealed record IgdEndpoint(Uri ControlUrl, string ServiceType, string DeviceBaseUrl);

public sealed class IgdDiscovery
{
  private static readonly XNamespace DeviceNs = "urn:schemas-upnp-org:device-1-0";
  private static readonly string[] SupportedServiceTypes =
  [
    "urn:schemas-upnp-org:service:WANIPConnection:2",
    "urn:schemas-upnp-org:service:WANIPConnection:1",
    "urn:schemas-upnp-org:service:WANPPPConnection:1"
  ];

  private static readonly int[] DescriptionPorts = [49152, 49000, 5000, 80, 2828, 8200];
  private static readonly string[] DescriptionPaths =
  [
    "/igd.xml",
    "/rootDesc.xml",
    "/RootDevice.xml",
    "/upnp/IGD.xml",
    "/description.xml",
    "/InternetGatewayDevice.xml"
  ];
  private static readonly TimeSpan DiscoveryBudget = TimeSpan.FromSeconds(5);
  private const int ProbeConcurrency = 4;

  private readonly HttpClient _http;

  public IgdDiscovery(HttpClient http)
  {
    _http = http;
  }

  public async Task<IgdEndpoint?> FromRouterAddressAsync(string router, CancellationToken ct)
  {
    var ip = await ResolveIpv4Async(router, ct);
    if (ip != null)
    {
      var viaSsdp = await FromUnicastSsdpAsync(ip, ct);
      if (viaSsdp != null) return viaSsdp;
    }

    using var outer = CancellationTokenSource.CreateLinkedTokenSource(ct);
    outer.CancelAfter(DiscoveryBudget);

    using var sem = new SemaphoreSlim(ProbeConcurrency);
    var tasks = new List<Task<IgdEndpoint?>>();
    foreach (var port in DescriptionPorts)
      foreach (var path in DescriptionPaths)
      {
        var url = new Uri($"http://{router}:{port}{path}");
        tasks.Add(ProbeAsync(url, sem, outer.Token));
      }

    while (tasks.Count > 0)
    {
      var completed = await Task.WhenAny(tasks);
      tasks.Remove(completed);
      var result = await completed;
      if (result != null)
      {
        outer.Cancel();
        try { await Task.WhenAll(tasks); }
        catch { }
        return result;
      }
    }
    return null;
  }

  private async Task<IgdEndpoint?> ProbeAsync(Uri url, SemaphoreSlim sem, CancellationToken ct)
  {
    try { await sem.WaitAsync(ct); }
    catch (OperationCanceledException) { return null; }
    try { return await TryResolveAsync(url, ct); }
    finally { sem.Release(); }
  }

  private static async Task<IPAddress?> ResolveIpv4Async(string host, CancellationToken ct)
  {
    if (IPAddress.TryParse(host, out var ip)) return ip;
    try
    {
      var addresses = await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, ct);
      return addresses.FirstOrDefault();
    }
    catch { return null; }
  }

  public Task<IgdEndpoint?> FromMulticastSsdpAsync(CancellationToken ct) =>
    SsdpSearchAsync(new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900), ct);

  public Task<IgdEndpoint?> FromUnicastSsdpAsync(IPAddress router, CancellationToken ct) =>
    SsdpSearchAsync(new IPEndPoint(router, 1900), ct);

  private async Task<IgdEndpoint?> SsdpSearchAsync(IPEndPoint target, CancellationToken ct)
  {
    using var udp = new UdpClient();
    udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
    udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

    var search = "M-SEARCH * HTTP/1.1\r\n"
               + $"HOST: {target}\r\n"
               + "MAN: \"ssdp:discover\"\r\n"
               + "MX: 2\r\n"
               + "ST: urn:schemas-upnp-org:device:InternetGatewayDevice:1\r\n\r\n";
    var payload = Encoding.UTF8.GetBytes(search);

    for (var i = 0; i < 3; i++)
      await udp.SendAsync(payload, target, ct);

    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeout.CancelAfter(TimeSpan.FromSeconds(3));

    try
    {
      while (!timeout.IsCancellationRequested)
      {
        var result = await udp.ReceiveAsync(timeout.Token);
        var response = Encoding.UTF8.GetString(result.Buffer);
        var location = ExtractHeader(response, "LOCATION");
        if (location != null && Uri.TryCreate(location, UriKind.Absolute, out var url))
        {
          var endpoint = await TryResolveAsync(url, ct);
          if (endpoint != null) return endpoint;
        }
      }
    }
    catch (OperationCanceledException) { }

    return null;
  }

  internal async Task<IgdEndpoint?> TryResolveAsync(Uri descriptionUrl, CancellationToken ct)
  {
    string xml;
    try
    {
      using var request = new HttpRequestMessage(HttpMethod.Get, descriptionUrl);
      using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
      timeout.CancelAfter(TimeSpan.FromSeconds(2));
      using var response = await _http.SendAsync(request, timeout.Token);
      if (!response.IsSuccessStatusCode) return null;
      xml = await response.Content.ReadAsStringAsync(timeout.Token);
    }
    catch
    {
      return null;
    }

    XDocument doc;
    try { doc = XDocument.Parse(xml); }
    catch { return null; }

    foreach (var serviceType in SupportedServiceTypes)
    {
      var service = doc.Descendants(DeviceNs + "service")
        .FirstOrDefault(s => (string?)s.Element(DeviceNs + "serviceType") == serviceType);
      var controlPath = service?.Element(DeviceNs + "controlURL")?.Value;
      if (string.IsNullOrWhiteSpace(controlPath)) continue;

      var baseUrl = new Uri(descriptionUrl, "/");
      var controlUrl = new Uri(baseUrl, controlPath);
      return new IgdEndpoint(controlUrl, serviceType, baseUrl.ToString());
    }

    return null;
  }

  internal static string? ExtractHeader(string response, string name)
  {
    foreach (var line in response.Split("\r\n"))
    {
      var sep = line.IndexOf(':');
      if (sep < 0) continue;
      var header = line[..sep].Trim();
      if (!header.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
      return line[(sep + 1)..].Trim();
    }
    return null;
  }
}
