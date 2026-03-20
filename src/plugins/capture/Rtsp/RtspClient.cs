using System.Buffers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Capture.Rtsp;

public enum RtspState
{
  Disconnected,
  Described,
  Setup,
  Playing,
  Teardown
}

public sealed class RtspClient : IAsyncDisposable
{
  private TcpClient? _tcp;
  private NetworkStream? _stream;
  private int _cseq;
  private string? _session;
  private string? _authHeader;
  private string? _realm;
  private string? _nonce;
  private string? _username;
  private string? _password;
  private Uri _uri = null!;

  public RtspState State { get; private set; } = RtspState.Disconnected;

  public async Task<string> ConnectAndDescribeAsync(
    string uri, string? username, string? password, CancellationToken ct)
  {
    _uri = new Uri(uri);
    _username = username;
    _password = password;

    _tcp = new TcpClient();
    var port = _uri.Port > 0 ? _uri.Port : 554;
    await _tcp.ConnectAsync(_uri.Host, port, ct);
    _stream = _tcp.GetStream();

    var response = await SendRequestAsync("DESCRIBE", _uri.AbsoluteUri, "Accept: application/sdp", ct);

    if (response.StatusCode == 401)
    {
      ParseAuthChallenge(response.Headers);
      response = await SendRequestAsync("DESCRIBE", _uri.AbsoluteUri, "Accept: application/sdp", ct);
    }

    if (response.StatusCode != 200)
      throw new InvalidOperationException($"DESCRIBE failed with status {response.StatusCode}");

    State = RtspState.Described;
    return response.Body;
  }

  public async Task SetupAsync(string controlUri, CancellationToken ct)
  {
    var absoluteControl = ResolveControlUri(controlUri);
    var transport = "Transport: RTP/AVP/TCP;unicast;interleaved=0-1";
    var response = await SendRequestAsync("SETUP", absoluteControl, transport, ct);

    if (response.StatusCode != 200)
      throw new InvalidOperationException($"SETUP failed with status {response.StatusCode}");

    foreach (var header in response.Headers)
    {
      if (header.StartsWith("Session:", StringComparison.OrdinalIgnoreCase))
      {
        var sessionValue = header["Session:".Length..].Trim();
        var semicolonIdx = sessionValue.IndexOf(';');
        _session = semicolonIdx >= 0 ? sessionValue[..semicolonIdx] : sessionValue;
      }
    }

    State = RtspState.Setup;
  }

  public async Task PlayAsync(CancellationToken ct)
  {
    var extraHeaders = $"Range: npt=0.000-";
    if (_session != null)
      extraHeaders = $"Session: {_session}\r\n{extraHeaders}";

    var response = await SendRequestAsync("PLAY", _uri.AbsoluteUri, extraHeaders, ct);

    if (response.StatusCode != 200)
      throw new InvalidOperationException($"PLAY failed with status {response.StatusCode}");

    State = RtspState.Playing;
  }

  public async Task TeardownAsync(CancellationToken ct)
  {
    if (_stream == null || State == RtspState.Disconnected || State == RtspState.Teardown)
      return;

    try
    {
      string? extra = _session != null ? $"Session: {_session}" : null;
      await SendRequestAsync("TEARDOWN", _uri.AbsoluteUri, extra, ct);
    }
    catch
    {
      // best effort
    }

    State = RtspState.Teardown;
  }

  public async Task<(byte Channel, ReadOnlyMemory<byte> Payload)?> ReadInterleavedFrameAsync(
    CancellationToken ct)
  {
    if (_stream == null)
      return null;

    var headerBuf = new byte[4];

    while (true)
    {
      var firstByte = await ReadByteAsync(ct);
      if (firstByte < 0)
        return null;

      if (firstByte == '$')
      {
        await ReadExactAsync(headerBuf.AsMemory(0, 3), ct);
        var channel = headerBuf[0];
        var length = (headerBuf[1] << 8) | headerBuf[2];

        var payload = new byte[length];
        await ReadExactAsync(payload, ct);
        return (channel, payload);
      }

      // skip RTSP response line that came mid-stream
      await SkipLineAsync((byte)firstByte, ct);
    }
  }

  private async Task<int> ReadByteAsync(CancellationToken ct)
  {
    var buf = new byte[1];
    var read = await _stream!.ReadAsync(buf, ct);
    return read == 0 ? -1 : buf[0];
  }

  private async Task ReadExactAsync(Memory<byte> buffer, CancellationToken ct)
  {
    var offset = 0;
    while (offset < buffer.Length)
    {
      var read = await _stream!.ReadAsync(buffer[offset..], ct);
      if (read == 0)
        throw new IOException("Connection closed");
      offset += read;
    }
  }

  private async Task SkipLineAsync(byte first, CancellationToken ct)
  {
    if (first == '\n') return;
    while (true)
    {
      var b = await ReadByteAsync(ct);
      if (b < 0 || b == '\n') break;
    }
  }

  private async Task<RtspResponse> SendRequestAsync(
    string method, string uri, string? extraHeaders, CancellationToken ct)
  {
    _cseq++;
    var sb = new StringBuilder();
    sb.Append($"{method} {uri} RTSP/1.0\r\n");
    sb.Append($"CSeq: {_cseq}\r\n");
    sb.Append("User-Agent: SimpleVMS/1.0\r\n");

    if (_authHeader != null)
      sb.Append(BuildAuthHeader(method, uri));

    if (_session != null && method != "SETUP" && !extraHeaders?.Contains("Session:") == true)
      sb.Append($"Session: {_session}\r\n");

    if (extraHeaders != null)
      sb.Append($"{extraHeaders}\r\n");

    sb.Append("\r\n");

    var bytes = Encoding.ASCII.GetBytes(sb.ToString());
    await _stream!.WriteAsync(bytes, ct);
    await _stream.FlushAsync(ct);

    return await ReadResponseAsync(ct);
  }

  private async Task<RtspResponse> ReadResponseAsync(CancellationToken ct)
  {
    var sb = new StringBuilder();
    var headers = new List<string>();
    int statusCode = 0;
    int contentLength = 0;

    // read status line
    var statusLine = await ReadLineAsync(ct);
    var parts = statusLine.Split(' ', 3);
    if (parts.Length >= 2)
      int.TryParse(parts[1], out statusCode);

    // read headers
    while (true)
    {
      var line = await ReadLineAsync(ct);
      if (string.IsNullOrEmpty(line))
        break;

      headers.Add(line);
      if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
        int.TryParse(line["Content-Length:".Length..].Trim(), out contentLength);
    }

    // read body
    var body = "";
    if (contentLength > 0)
    {
      var bodyBuf = new byte[contentLength];
      await ReadExactAsync(bodyBuf, ct);
      body = Encoding.UTF8.GetString(bodyBuf);
    }

    return new RtspResponse(statusCode, headers, body);
  }

  private async Task<string> ReadLineAsync(CancellationToken ct)
  {
    var sb = new StringBuilder();
    while (true)
    {
      var b = await ReadByteAsync(ct);
      if (b < 0 || b == '\n')
        break;
      if (b != '\r')
        sb.Append((char)b);
    }
    return sb.ToString();
  }

  private void ParseAuthChallenge(IReadOnlyList<string> headers)
  {
    foreach (var header in headers)
    {
      if (!header.StartsWith("WWW-Authenticate:", StringComparison.OrdinalIgnoreCase))
        continue;

      var value = header["WWW-Authenticate:".Length..].Trim();

      if (value.StartsWith("Digest", StringComparison.OrdinalIgnoreCase))
      {
        _authHeader = "Digest";
        _realm = ExtractQuotedParam(value, "realm");
        _nonce = ExtractQuotedParam(value, "nonce");
      }
      else if (value.StartsWith("Basic", StringComparison.OrdinalIgnoreCase))
      {
        _authHeader = "Basic";
        _realm = ExtractQuotedParam(value, "realm");
      }
    }
  }

  private string? BuildAuthHeader(string method, string uri)
  {
    if (_authHeader == "Basic" && _username != null)
    {
      var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_username}:{_password}"));
      return $"Authorization: Basic {credentials}\r\n";
    }

    if (_authHeader == "Digest" && _username != null && _nonce != null && _realm != null)
    {
      var ha1 = Md5Hash($"{_username}:{_realm}:{_password}");
      var ha2 = Md5Hash($"{method}:{uri}");
      var response = Md5Hash($"{ha1}:{_nonce}:{ha2}");
      return $"Authorization: Digest username=\"{_username}\", realm=\"{_realm}\", " +
             $"nonce=\"{_nonce}\", uri=\"{uri}\", response=\"{response}\"\r\n";
    }

    return null;
  }

  private static string Md5Hash(string input)
  {
    var bytes = MD5.HashData(Encoding.UTF8.GetBytes(input));
    return Convert.ToHexString(bytes).ToLowerInvariant();
  }

  private static string? ExtractQuotedParam(string header, string param)
  {
    var key = $"{param}=\"";
    var idx = header.IndexOf(key, StringComparison.OrdinalIgnoreCase);
    if (idx < 0)
      return null;

    var start = idx + key.Length;
    var end = header.IndexOf('"', start);
    return end > start ? header[start..end] : null;
  }

  private string ResolveControlUri(string controlUri)
  {
    if (controlUri.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase))
      return controlUri;

    var baseUri = _uri.AbsoluteUri;
    if (!baseUri.EndsWith('/'))
      baseUri += "/";

    return baseUri + controlUri;
  }

  public async ValueTask DisposeAsync()
  {
    if (State == RtspState.Playing)
    {
      try { await TeardownAsync(CancellationToken.None); }
      catch { /* best effort */ }
    }

    _stream?.Dispose();
    _tcp?.Dispose();
    State = RtspState.Disconnected;
  }

  private sealed record RtspResponse(int StatusCode, IReadOnlyList<string> Headers, string Body);
}
