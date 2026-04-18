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
  private int _sessionTimeout = 60;
  private string? _authHeader;
  private string? _realm;
  private string? _nonce;
  private string? _username;
  private string? _password;
  private Uri _uri = null!;
  private Timer? _keepaliveTimer;
  private readonly Lock _writeLock = new();
  private readonly byte[] _readBuf = new byte[8192];
  private int _readBufPos;
  private int _readBufLen;

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
    LastSdp = response.Body;
    return response.Body;
  }

  public string? LastSdp { get; private set; }

  public Task SetupAsync(string controlUri, CancellationToken ct) =>
    SetupAsync(controlUri, 0, ct);

  public async Task<int> SetupAsync(string controlUri, int interleavedBase, CancellationToken ct)
  {
    var absoluteControl = ResolveControlUri(controlUri);
    var transport = $"Transport: RTP/AVP/TCP;unicast;interleaved={interleavedBase}-{interleavedBase + 1}";
    var response = await SendRequestAsync("SETUP", absoluteControl, transport, ct);

    if (response.StatusCode != 200)
      throw new InvalidOperationException($"SETUP failed with status {response.StatusCode}");

    var actualChannel = interleavedBase;

    foreach (var header in response.Headers)
    {
      if (header.StartsWith("Session:", StringComparison.OrdinalIgnoreCase))
      {
        var sessionValue = header["Session:".Length..].Trim();
        var semicolonIdx = sessionValue.IndexOf(';');
        _session = semicolonIdx >= 0 ? sessionValue[..semicolonIdx] : sessionValue;

        var timeoutIdx = sessionValue.IndexOf("timeout=", StringComparison.OrdinalIgnoreCase);
        if (timeoutIdx >= 0)
        {
          var timeoutStr = sessionValue[(timeoutIdx + 8)..];
          var endIdx = timeoutStr.IndexOfAny([';', ' ', ',']);
          if (endIdx > 0) timeoutStr = timeoutStr[..endIdx];
          if (int.TryParse(timeoutStr, out var timeout) && timeout > 0)
            _sessionTimeout = timeout;
        }
      }
      else if (header.StartsWith("Transport:", StringComparison.OrdinalIgnoreCase))
      {
        var transportValue = header["Transport:".Length..].Trim();
        var interleavedIdx = transportValue.IndexOf("interleaved=", StringComparison.OrdinalIgnoreCase);
        if (interleavedIdx >= 0)
        {
          var channelStr = transportValue[(interleavedIdx + 12)..];
          var dashIdx = channelStr.IndexOf('-');
          var endIdx = channelStr.IndexOfAny([';', ' ', ',']);
          if (endIdx > 0) channelStr = channelStr[..endIdx];
          if (dashIdx > 0) channelStr = channelStr[..dashIdx];
          if (int.TryParse(channelStr, out var ch))
            actualChannel = ch;
        }
      }
    }

    State = RtspState.Setup;
    return actualChannel;
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
    StartKeepalive();
  }

  public async Task TeardownAsync(CancellationToken ct)
  {
    _keepaliveTimer?.Dispose();
    _keepaliveTimer = null;

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

  private void StartKeepalive()
  {
    var interval = TimeSpan.FromSeconds(Math.Max(_sessionTimeout / 2, 5));
    _keepaliveTimer = new Timer(_ => SendKeepalive(), null, interval, interval);
  }

  private void SendKeepalive()
  {
    if (_stream == null || State != RtspState.Playing)
      return;

    try
    {
      var cseq = Interlocked.Increment(ref _cseq);
      var sb = new StringBuilder();
      sb.Append($"OPTIONS {_uri.AbsoluteUri} RTSP/1.0\r\n");
      sb.Append($"CSeq: {cseq}\r\n");
      sb.Append("User-Agent: SimpleVMS/1.0\r\n");
      if (_session != null)
        sb.Append($"Session: {_session}\r\n");
      sb.Append("\r\n");

      var bytes = Encoding.ASCII.GetBytes(sb.ToString());
      lock (_writeLock)
      {
        _stream.Write(bytes);
        _stream.Flush();
      }
    }
    catch
    {
      // connection is dead, read loop will detect it
    }
  }

  public async Task<(byte Channel, ReadOnlyMemory<byte> Payload)?> ReadInterleavedFrameAsync(
    CancellationToken ct)
  {
    if (_stream == null)
      return null;

    while (true)
    {
      var firstByte = await BufferedReadByteAsync(ct);
      if (firstByte < 0)
        return null;

      if (firstByte == '$')
      {
        var ch = await BufferedReadByteAsync(ct);
        var hi = await BufferedReadByteAsync(ct);
        var lo = await BufferedReadByteAsync(ct);
        if (ch < 0 || hi < 0 || lo < 0)
          return null;

        var length = (hi << 8) | lo;
        var payload = new byte[length];
        await BufferedReadExactAsync(payload, ct);
        return ((byte)ch, payload);
      }

      if (firstByte != '\n')
      {
        while (true)
        {
          var b = await BufferedReadByteAsync(ct);
          if (b < 0 || b == '\n') break;
        }
      }
    }
  }

  private async ValueTask<int> BufferedReadByteAsync(CancellationToken ct)
  {
    if (_readBufPos < _readBufLen)
      return _readBuf[_readBufPos++];

    _readBufLen = await _stream!.ReadAsync(_readBuf, ct);
    _readBufPos = 0;
    if (_readBufLen == 0)
      return -1;
    return _readBuf[_readBufPos++];
  }

  private async ValueTask BufferedReadExactAsync(Memory<byte> buffer, CancellationToken ct)
  {
    var remaining = buffer;
    while (remaining.Length > 0)
    {
      var buffered = _readBufLen - _readBufPos;
      if (buffered > 0)
      {
        var toCopy = Math.Min(buffered, remaining.Length);
        _readBuf.AsMemory(_readBufPos, toCopy).CopyTo(remaining);
        _readBufPos += toCopy;
        remaining = remaining[toCopy..];
      }
      else
      {
        if (remaining.Length >= _readBuf.Length)
        {
          var read = await _stream!.ReadAsync(remaining, ct);
          if (read == 0)
            throw new IOException("Connection closed");
          remaining = remaining[read..];
        }
        else
        {
          _readBufLen = await _stream!.ReadAsync(_readBuf, ct);
          _readBufPos = 0;
          if (_readBufLen == 0)
            throw new IOException("Connection closed");
        }
      }
    }
  }

  private async Task<int> ReadByteAsync(CancellationToken ct) =>
    await BufferedReadByteAsync(ct);

  private async Task ReadExactAsync(Memory<byte> buffer, CancellationToken ct) =>
    await BufferedReadExactAsync(buffer, ct);

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

    var statusLine = await ReadLineAsync(ct);
    var parts = statusLine.Split(' ', 3);
    if (parts.Length >= 2)
      int.TryParse(parts[1], out statusCode);

    while (true)
    {
      var line = await ReadLineAsync(ct);
      if (string.IsNullOrEmpty(line))
        break;

      headers.Add(line);
      if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
        int.TryParse(line["Content-Length:".Length..].Trim(), out contentLength);
    }

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
    _keepaliveTimer?.Dispose();
    _keepaliveTimer = null;

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
