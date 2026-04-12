using System.Buffers.Binary;
using System.Net.Sockets;
using Client.Core.Platform;
using Microsoft.Extensions.Logging;
using Shared.Protocol;

namespace Client.Core.Tunnel;

public sealed class TunnelService : ITunnelService, IAsyncDisposable
{
  private const uint ProtocolVersion = 1;
  private static readonly TimeSpan[] BackoffSteps =
    [
      TimeSpan.FromMilliseconds(100),
      TimeSpan.FromMilliseconds(200),
      TimeSpan.FromMilliseconds(400),
      TimeSpan.FromMilliseconds(800),
      TimeSpan.FromMilliseconds(1600),
      TimeSpan.FromMilliseconds(3200),
      TimeSpan.FromMilliseconds(6400),
      TimeSpan.FromMilliseconds(12800),
      TimeSpan.FromSeconds(30)
    ];
  private static readonly TimeSpan ReprobeInterval = TimeSpan.FromSeconds(60);

  private readonly ICredentialStore _credentials;
  private readonly ITransportFactory _transport;
  private readonly ILogger<TunnelService> _logger;
  private readonly Lock _lock = new();

  private ConnectionState _state = ConnectionState.Disconnected;
  private uint _generation;
  private StreamMuxer? _muxer;
  private TransportConnection? _connection;
  private CancellationTokenSource? _connectionCts;
  private CancellationTokenSource? _lifecycleCts;
  private Task? _readLoop;
  private Task? _keepalive;
  private Task? _reprobeLoop;
  private Task? _reconnectLoop;
  private int _connectedAddressIndex;
  private ConnectionOptions _options = new();
  private volatile bool _autoReconnect;
  private bool _disposed;

  public ConnectionState State
  {
    get { lock (_lock) return _state; }
  }

  public int ConnectedAddressIndex
  {
    get { lock (_lock) return _connectedAddressIndex; }
  }

  public event Action<ConnectionState>? StateChanged;

  public uint Generation
  {
    get { lock (_lock) return _generation; }
  }

  public TunnelService(
    ICredentialStore credentials,
    ITransportFactory transport,
    ILogger<TunnelService> logger)
  {
    _credentials = credentials;
    _transport = transport;
    _logger = logger;
  }

  public async Task ConnectAsync(ConnectionOptions options, CancellationToken ct)
  {
    _logger.LogDebug("ConnectAsync started");
    _options = options;
    _lifecycleCts?.Dispose();
    _autoReconnect = true;
    _lifecycleCts = new CancellationTokenSource();
    await ConnectCoreAsync(ct);
    _logger.LogDebug("ConnectAsync completed");
  }

  public async Task DisconnectAsync(CancellationToken ct = default)
  {
    _logger.LogDebug("DisconnectAsync started");
    _autoReconnect = false;
    _lifecycleCts?.Cancel();
    await TeardownConnectionAsync();
    if (_reconnectLoop != null)
    {
      _logger.LogDebug("Awaiting reconnect loop completion");
      try { await _reconnectLoop.WaitAsync(ct); }
      catch (OperationCanceledException) { }
    }
    _reconnectLoop = null;
    _lifecycleCts?.Dispose();
    _lifecycleCts = null;
    SetState(ConnectionState.Disconnected);
    _logger.LogDebug("DisconnectAsync completed");
  }

  public Task<MuxStream> OpenStreamAsync(
    ushort streamType, ReadOnlyMemory<byte> payload, CancellationToken ct)
  {
    StreamMuxer? muxer;
    lock (_lock) muxer = _muxer;

    if (muxer == null)
    {
      _logger.LogError("OpenStreamAsync called while not connected");
      throw new InvalidOperationException("Not connected");
    }

    var (streamId, reader) = muxer.OpenStream(streamType, payload);
    return Task.FromResult(new MuxStream(muxer, streamId, reader));
  }

  private async Task ConnectCoreAsync(CancellationToken ct)
  {
    _logger.LogDebug("ConnectCoreAsync entry");
    SetState(ConnectionState.Connecting);
    try
    {

    _logger.LogDebug("Loading credentials");
    var creds = await _credentials.LoadAsync();
    if (creds == null)
      throw new InvalidOperationException("No credentials available");
    _logger.LogDebug(
      "Credentials loaded, {AddressCount} addresses available", creds.Addresses.Length);

    var (connection, addressIndex) = await ConnectToServerAsync(creds, ct);

    _logger.LogDebug("Exchanging protocol version");
    if (!await ExchangeVersionAsync(connection.Stream, ct))
    {
      _logger.LogWarning("Protocol version mismatch, disposing connection");
      connection.Dispose();
      throw new InvalidOperationException("Protocol version mismatch");
    }
    _logger.LogDebug("Version exchange succeeded");

    var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(
      _lifecycleCts?.Token ?? CancellationToken.None);
    var muxer = new StreamMuxer(connection.Stream, _logger, 1);

    lock (_lock)
    {
      _connection = connection;
      _muxer = muxer;
      _connectionCts = connectionCts;
      _connectedAddressIndex = addressIndex;
      _generation++;
      _logger.LogDebug("Connection established, generation {Generation}", _generation);
    }

    var stream0 = muxer.GetOrCreateStream(0);
    _keepalive = KeepaliveHandler.RunAsync(
      stream0,
      (msg, c) => muxer.SendAsync(0, 0, msg, c),
      () =>
      {
        _logger.LogDebug("Keepalive timeout");
        connectionCts.Cancel();
      },
      _logger, connectionCts.Token);

    _readLoop = RunReadLoopAsync(muxer, connectionCts.Token);

    if (_options.ReprobeEnabled && addressIndex > 0)
    {
      _logger.LogDebug(
        "Connected to fallback address index {Index}, starting reprobe", addressIndex);
      _reprobeLoop = ReprobeEarlierAddressesAsync(creds, addressIndex, connectionCts.Token);
    }

    SetState(ConnectionState.Connected);

    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "ConnectCoreAsync failed");
      SetState(ConnectionState.Disconnected);
      throw;
    }
  }

  private async Task RunReadLoopAsync(StreamMuxer muxer, CancellationToken ct)
  {
    _logger.LogDebug("Read loop started");
    try
    {
      await muxer.RunReadLoopAsync(ct);
    }
    catch (OperationCanceledException)
    {
      _logger.LogDebug("Read loop cancelled");
    }
    catch (IOException ex)
    {
      _logger.LogWarning(ex, "Read loop terminated with IO error");
    }
    finally
    {
      _logger.LogDebug(
        "Read loop ended, autoReconnect={AutoReconnect}", _autoReconnect);
      if (_autoReconnect)
        _reconnectLoop = ReconnectLoopAsync();
    }
  }

  private async Task ReconnectLoopAsync()
  {
    _logger.LogDebug("ReconnectLoopAsync entry");
    await TeardownConnectionAsync();
    SetState(ConnectionState.Disconnected);

    var token = _lifecycleCts?.Token ?? CancellationToken.None;

    for (var attempt = 0; _autoReconnect && !token.IsCancellationRequested; attempt++)
    {
      var delay = BackoffSteps[Math.Min(attempt, BackoffSteps.Length - 1)];
      _logger.LogDebug(
        "Reconnect attempt {Attempt}, backoff {DelayMs}ms",
        attempt + 1, delay.TotalMilliseconds);

      try { await Task.Delay(delay, token); }
      catch (OperationCanceledException)
      {
        _logger.LogDebug("Reconnect loop cancelled during backoff");
        return;
      }

      if (!_autoReconnect)
      {
        _logger.LogDebug("Reconnect loop exiting, autoReconnect disabled");
        return;
      }

      try
      {
        await ConnectCoreAsync(token);
        _logger.LogDebug("Reconnect succeeded on attempt {Attempt}", attempt + 1);
        return;
      }
      catch (OperationCanceledException)
      {
        _logger.LogDebug("Reconnect loop cancelled during connect");
        return;
      }
      catch (Exception ex)
      {
        _logger.LogDebug(ex, "Reconnect attempt {Attempt} failed", attempt + 1);
      }
    }
    _logger.LogDebug("Reconnect loop exhausted");
  }

  private async Task<(TransportConnection Connection, int AddressIndex)> ConnectToServerAsync(
    CredentialData creds, CancellationToken ct)
  {
    _logger.LogDebug("ConnectToServerAsync trying {Count} addresses", creds.Addresses.Length);

    var hint = _options.LastSuccessfulIndex;
    if (hint >= 0 && hint < creds.Addresses.Length)
    {
      ct.ThrowIfCancellationRequested();
      try
      {
        var connection = await _transport.ConnectAsync(creds.Addresses[hint], creds, ct);
        _logger.LogInformation("Connected to preferred address {Address}", creds.Addresses[hint]);
        return (connection, hint);
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        _logger.LogDebug(ex, "Preferred address {Address} failed, trying all",
          creds.Addresses[hint]);
      }
    }

    for (var i = 0; i < creds.Addresses.Length; i++)
    {
      ct.ThrowIfCancellationRequested();

      try
      {
        var connection = await _transport.ConnectAsync(creds.Addresses[i], creds, ct);
        _logger.LogInformation("Connected to {Address}", creds.Addresses[i]);
        return (connection, i);
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        _logger.LogDebug(ex, "Failed to connect to {Address}", creds.Addresses[i]);
      }
    }

    _logger.LogError("All {Count} server addresses exhausted", creds.Addresses.Length);
    throw new InvalidOperationException("Could not connect to any server address");
  }

  private static async Task<bool> ExchangeVersionAsync(Stream transport, CancellationToken ct)
  {
    var versionPayload = new byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(versionPayload, ProtocolVersion);
    var frame = new byte[MessageEnvelope.MuxHeaderSize + 4];
    MessageEnvelope.WriteMuxHeader(frame, 0, 0, 4);
    versionPayload.CopyTo(frame.AsSpan(MessageEnvelope.MuxHeaderSize));
    await transport.WriteAsync(frame, ct);

    byte[] responseHeader;
    try
    {
      responseHeader = new byte[MessageEnvelope.MuxHeaderSize];
      await transport.ReadExactlyAsync(responseHeader, ct);
    }
    catch (EndOfStreamException)
    {
      return false;
    }
    var (streamId, _, payloadLength) = MessageEnvelope.ReadMuxHeader(responseHeader);

    if (streamId != 0 || payloadLength < 4)
      return false;

    var responsePayload = new byte[payloadLength];
    await transport.ReadExactlyAsync(responsePayload, ct);
    var serverVersion = BinaryPrimitives.ReadUInt32LittleEndian(responsePayload);

    return serverVersion == ProtocolVersion;
  }

  private async Task ReprobeEarlierAddressesAsync(
    CredentialData creds, int currentIndex, CancellationToken ct)
  {
    _logger.LogDebug(
      "Reprobe loop started, checking {Count} earlier addresses", currentIndex);
    while (!ct.IsCancellationRequested)
    {
      try { await Task.Delay(ReprobeInterval, ct); }
      catch (OperationCanceledException) { return; }

      for (var i = 0; i < currentIndex; i++)
      {
        try
        {
          using var probe = new TcpClient();
          var (host, port) = TlsTransportFactory.ParseAddress(creds.Addresses[i]);
          using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
          probeCts.CancelAfter(TimeSpan.FromSeconds(3));
          await probe.ConnectAsync(host, port, probeCts.Token);

          _logger.LogInformation(
            "Earlier address {Address} reachable, reconnecting", creds.Addresses[i]);
          lock (_lock) _reprobeLoop = null;
          _connectionCts?.Cancel();
          return;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
          _logger.LogDebug(ex, "Reprobe of {Address} failed", creds.Addresses[i]);
        }
      }
    }
  }

  private async Task TeardownConnectionAsync()
  {
    _logger.LogDebug("TeardownConnectionAsync entry");

    StreamMuxer? muxer;
    CancellationTokenSource? cts;
    TransportConnection? connection;
    Task? readLoop;
    Task? keepalive;
    Task? reprobe;

    lock (_lock)
    {
      muxer = _muxer;
      cts = _connectionCts;
      connection = _connection;
      readLoop = _readLoop;
      keepalive = _keepalive;
      reprobe = _reprobeLoop;
      _muxer = null;
      _connectionCts = null;
      _connection = null;
      _readLoop = null;
      _keepalive = null;
      _reprobeLoop = null;
    }

    cts?.Cancel();

    if (muxer != null)
      await muxer.DisposeAsync();

    if (readLoop != null)
      _ = readLoop.ContinueWith(_ => { }, TaskScheduler.Default);
    if (keepalive != null)
      _ = keepalive.ContinueWith(_ => { }, TaskScheduler.Default);
    if (reprobe != null)
      _ = reprobe.ContinueWith(_ => { }, TaskScheduler.Default);

    if (connection != null)
      await connection.DisposeAsync();
    cts?.Dispose();

    _logger.LogDebug("TeardownConnectionAsync completed");
  }

  private void SetState(ConnectionState state)
  {
    _logger.LogInformation("Tunnel state -> {State}", state);
    lock (_lock) _state = state;
    StateChanged?.Invoke(state);
  }

  public async ValueTask DisposeAsync()
  {
    if (_disposed) return;
    _logger.LogDebug("DisposeAsync started");
    _disposed = true;
    _autoReconnect = false;
    _lifecycleCts?.Cancel();
    await TeardownConnectionAsync();
    if (_reconnectLoop != null)
    {
      try { await _reconnectLoop; }
      catch (OperationCanceledException) { }
    }
    _lifecycleCts?.Dispose();
    _logger.LogDebug("DisposeAsync completed");
  }

}
