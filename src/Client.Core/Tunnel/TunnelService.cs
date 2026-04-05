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
  private volatile bool _autoReconnect;
  private bool _disposed;

  public ConnectionState State
  {
    get { lock (_lock) return _state; }
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

  public async Task ConnectAsync(CancellationToken ct)
  {
    _lifecycleCts?.Dispose();
    _autoReconnect = true;
    _lifecycleCts = new CancellationTokenSource();
    await ConnectCoreAsync(ct);
  }

  public async Task DisconnectAsync()
  {
    _autoReconnect = false;
    _lifecycleCts?.Cancel();
    await TeardownConnectionAsync();
    if (_reconnectLoop != null)
    {
      try { await _reconnectLoop; }
      catch (OperationCanceledException) { }
    }
    _reconnectLoop = null;
    _lifecycleCts?.Dispose();
    _lifecycleCts = null;
    SetState(ConnectionState.Disconnected);
  }

  public Task<MuxStream> OpenStreamAsync(
    ushort streamType, ReadOnlyMemory<byte> payload, CancellationToken ct)
  {
    StreamMuxer? muxer;
    lock (_lock) muxer = _muxer;

    if (muxer == null)
      throw new InvalidOperationException("Not connected");

    var (streamId, reader) = muxer.OpenStream(streamType, payload);
    return Task.FromResult(new MuxStream(muxer, streamId, reader));
  }

  private async Task ConnectCoreAsync(CancellationToken ct)
  {
    SetState(ConnectionState.Connecting);
    try
    {

    var creds = await _credentials.LoadAsync();
    if (creds == null)
      throw new InvalidOperationException("No credentials available");

    var (connection, addressIndex) = await ConnectToServerAsync(creds, ct);

    if (!await ExchangeVersionAsync(connection.Stream, ct))
    {
      connection.Dispose();
      throw new InvalidOperationException("Protocol version mismatch");
    }

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

    if (addressIndex > 0)
      _reprobeLoop = ReprobeEarlierAddressesAsync(creds, addressIndex, connectionCts.Token);

    SetState(ConnectionState.Connected);

    }
    catch
    {
      SetState(ConnectionState.Disconnected);
      throw;
    }
  }

  private async Task RunReadLoopAsync(StreamMuxer muxer, CancellationToken ct)
  {
    try
    {
      await muxer.RunReadLoopAsync(ct);
    }
    catch (OperationCanceledException) { }
    catch (IOException) { }
    finally
    {
      if (_autoReconnect)
        _reconnectLoop = ReconnectLoopAsync();
    }
  }

  private async Task ReconnectLoopAsync()
  {
    await TeardownConnectionAsync();
    SetState(ConnectionState.Disconnected);

    var token = _lifecycleCts?.Token ?? CancellationToken.None;

    for (var attempt = 0; _autoReconnect && !token.IsCancellationRequested; attempt++)
    {
      var delay = BackoffSteps[Math.Min(attempt, BackoffSteps.Length - 1)];

      try { await Task.Delay(delay, token); }
      catch (OperationCanceledException) { return; }

      if (!_autoReconnect) return;

      try
      {
        await ConnectCoreAsync(token);
        return;
      }
      catch (OperationCanceledException) { return; }
      catch (Exception ex)
      {
        _logger.LogDebug(ex, "Reconnect attempt {Attempt} failed", attempt + 1);
      }
    }
  }

  private async Task<(TransportConnection Connection, int AddressIndex)> ConnectToServerAsync(
    CredentialData creds, CancellationToken ct)
  {
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
        catch (Exception) when (!ct.IsCancellationRequested) { }
      }
    }
  }

  private async Task TeardownConnectionAsync()
  {
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
    {
      try { await readLoop; }
      catch (OperationCanceledException) { }
    }
    if (keepalive != null)
    {
      try { await keepalive; }
      catch (OperationCanceledException) { }
    }
    if (reprobe != null)
    {
      try { await reprobe; }
      catch (OperationCanceledException) { }
    }

    if (connection != null)
      await connection.DisposeAsync();
    cts?.Dispose();
  }

  private void SetState(ConnectionState state)
  {
    lock (_lock) _state = state;
    StateChanged?.Invoke(state);
  }

  public async ValueTask DisposeAsync()
  {
    if (_disposed) return;
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
  }

}
