using System.Buffers.Binary;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Server.Core;
using Server.Core.Routing;
using Server.Plugins;
using Server.Streaming;
using Server.Tunnel.Handlers;
using Shared.Models;
using Shared.Models.Events;
using Shared.Protocol;

namespace Server.Tunnel;

public sealed class TunnelService
{
  private const uint ProtocolVersion = 1;

  private readonly ICertificateService _certs;
  private readonly ServerEndpoints _endpoints;
  private readonly IPluginHost _plugins;
  private readonly IEventBus _eventBus;
  private readonly ConnectionTracker _connections;
  private readonly ApiDispatcher _dispatcher;
  private readonly StreamTapRegistry _tapRegistry;
  private readonly IServiceProvider _services;
  private readonly ILoggerFactory _loggerFactory;
  private readonly ILogger _logger;
  private readonly ClientValidator _validator;

  private TcpListener? _listener;
  private CancellationTokenSource? _cts;
  private Task? _acceptLoop;

  public TunnelService(
    ICertificateService certs,
    ServerEndpoints endpoints,
    IPluginHost plugins,
    IEventBus eventBus,
    ConnectionTracker connections,
    ApiDispatcher dispatcher,
    StreamTapRegistry tapRegistry,
    IServiceProvider services,
    ILoggerFactory loggerFactory)
  {
    _certs = certs;
    _endpoints = endpoints;
    _plugins = plugins;
    _eventBus = eventBus;
    _connections = connections;
    _dispatcher = dispatcher;
    _tapRegistry = tapRegistry;
    _services = services;
    _loggerFactory = loggerFactory;
    _logger = loggerFactory.CreateLogger<TunnelService>();
    _validator = new ClientValidator(plugins, _logger);
  }

  public Task StartAsync(CancellationToken ct)
  {
    _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

    _listener = new TcpListener(IPAddress.Any, _endpoints.TunnelPort);
    _listener.Start();

    _endpoints.TunnelPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
    _logger.LogInformation("Tunnel listening on port {Port}", _endpoints.TunnelPort);
    _acceptLoop = AcceptLoopAsync(_cts.Token);
    return Task.CompletedTask;
  }

  public async Task StopAsync()
  {
    _cts?.Cancel();
    _listener?.Stop();

    if (_acceptLoop != null)
    {
      try { await _acceptLoop; }
      catch (OperationCanceledException) { }
      _acceptLoop = null;
    }

    _cts?.Dispose();
    _cts = null;

    _logger.LogInformation("Tunnel stopped");
  }

  private async Task AcceptLoopAsync(CancellationToken ct)
  {
    while (!ct.IsCancellationRequested)
    {
      TcpClient client;
      try
      {
        client = await _listener!.AcceptTcpClientAsync(ct);
      }
      catch (OperationCanceledException) { break; }
      catch (ObjectDisposedException) { break; }
      catch (SocketException ex)
      {
        _logger.LogWarning(ex, "Failed to accept TCP connection");
        continue;
      }

      _ = HandleConnectionAsync(client, ct);
    }
  }

  private async Task HandleConnectionAsync(TcpClient client, CancellationToken ct)
  {
    Guid clientId = default;
    SslStream? sslStream = null;

    try
    {
      var networkStream = client.GetStream();
      sslStream = new SslStream(networkStream, false, ValidateClientCert);

      await sslStream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
      {
        ServerCertificate = _certs.ServerCert,
        ClientCertificateRequired = true,
        CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
        EnabledSslProtocols = SslProtocols.Tls13
      }, ct);

      var remoteCert = sslStream.RemoteCertificate;
      if (remoteCert == null)
      {
        _logger.LogDebug("No client certificate presented");
        return;
      }

      var serial = remoteCert.GetSerialNumberString().ToLowerInvariant();
      var validationResult = await _validator.ValidateAsync(serial, ct);
      if (validationResult.IsT1)
      {
        _logger.LogDebug("Client validation failed for serial {Serial}: {Error}",
          serial, validationResult.AsT1.Message);
        return;
      }

      clientId = validationResult.AsT0;
      _connections.SetConnected(clientId, true);

      var timestamp = DateTimeOffset.UtcNow.ToUnixMicroseconds();
      var remoteAddress = client.Client.RemoteEndPoint?.ToString() ?? "unknown";

      await _eventBus.PublishAsync(new ClientConnected
      {
        ClientId = clientId,
        RemoteAddress = remoteAddress,
        Timestamp = timestamp
      }, ct);

      _logger.LogInformation("Client {ClientId} connected from {Address}", clientId, remoteAddress);

      using var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
      var connectionCt = connectionCts.Token;

      await using var muxer = new StreamMuxer(sslStream, _logger);

      using var versionCts = CancellationTokenSource.CreateLinkedTokenSource(connectionCt);
      versionCts.CancelAfter(TimeSpan.FromSeconds(10));
      try
      {
        if (!await ExchangeVersionAsync(sslStream, versionCts.Token))
          return;
      }
      catch (OperationCanceledException) when (versionCts.IsCancellationRequested && !connectionCt.IsCancellationRequested)
      {
        _logger.LogDebug("Client {ClientId} timed out during version exchange", clientId);
        return;
      }

      muxer.OnNewStream = (streamType, streamId, reader, streamCt) =>
        HandleStreamAsync(streamType, streamId, reader, muxer, streamCt);

      var stream0 = muxer.GetOrCreateStream(0);
      var keepaliveTask = KeepaliveHandler.RunAsync(
        stream0,
        (msg, c) => muxer.SendAsync(0, 0, msg, c),
        () =>
        {
          _logger.LogDebug("Keepalive timeout for client {ClientId}", clientId);
          connectionCts.Cancel();
        },
        _loggerFactory.CreateLogger("Keepalive"), connectionCt);

      var readTask = muxer.RunReadLoopAsync(connectionCt);
      await Task.WhenAny(readTask, keepaliveTask);
      connectionCts.Cancel();
      try { await readTask; } catch (OperationCanceledException) { }
      try { await keepaliveTask; } catch (OperationCanceledException) { }
    }
    catch (AuthenticationException ex)
    {
      _logger.LogDebug(ex, "TLS handshake failed");
    }
    catch (OperationCanceledException) { }
    catch (IOException) { }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error handling connection for client {ClientId}", clientId);
    }
    finally
    {
      if (clientId != default)
      {
        _connections.Remove(clientId);

        var timestamp = DateTimeOffset.UtcNow.ToUnixMicroseconds();
        try
        {
          await _eventBus.PublishAsync(new ClientDisconnected
          {
            ClientId = clientId,
            Timestamp = timestamp
          }, CancellationToken.None);
        }
        catch { }

        _logger.LogInformation("Client {ClientId} disconnected", clientId);
      }

      if (sslStream != null)
        await sslStream.DisposeAsync();
      client.Dispose();
    }
  }

  private async Task<bool> ExchangeVersionAsync(SslStream transport, CancellationToken ct)
  {
    var header = new byte[MessageEnvelope.MuxHeaderSize];
    await transport.ReadExactlyAsync(header, ct);
    var (streamId, _, payloadLength) = MessageEnvelope.ReadMuxHeader(header);

    if (streamId != 0 || payloadLength < 4)
    {
      _logger.LogWarning("Invalid version announcement from client");
      return false;
    }

    var payload = new byte[payloadLength];
    await transport.ReadExactlyAsync(payload, ct);
    var clientVersion = BinaryPrimitives.ReadUInt32LittleEndian(payload);

    var serverPayload = new byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(serverPayload, ProtocolVersion);
    var responseFrame = new byte[MessageEnvelope.MuxHeaderSize + 4];
    MessageEnvelope.WriteMuxHeader(responseFrame, 0, 0, 4);
    serverPayload.CopyTo(responseFrame.AsSpan(MessageEnvelope.MuxHeaderSize));
    await transport.WriteAsync(responseFrame, ct);

    if (clientVersion != ProtocolVersion)
    {
      _logger.LogInformation(
        "Rejected client with protocol version {ClientVersion} (server: {ServerVersion})",
        clientVersion, ProtocolVersion);
      return false;
    }

    return true;
  }

  private async Task HandleStreamAsync(
    ushort streamType,
    uint streamId,
    ChannelReader<MuxMessage> reader,
    StreamMuxer muxer,
    CancellationToken ct)
  {
    try
    {
      switch (streamType)
      {
        case StreamTypes.ApiRequest:
          try
          {
            await ApiHandler.RunAsync(reader,
              (flags, payload, c) => muxer.SendAsync(streamId, flags, payload, c),
              _dispatcher, _services,
              _loggerFactory.CreateLogger("ApiStream"), ct);
          }
          finally
          {
            await muxer.SendFinAsync(streamId, ct);
          }
          break;

        case StreamTypes.LiveSubscribe:
        {
          var sink = new TunnelStreamSink(muxer, streamId);
          try
          {
            await LiveHandler.RunAsync(reader, sink, _tapRegistry,
              _loggerFactory.CreateLogger("LiveStream"), ct);
          }
          finally
          {
            sink.Close();
            await muxer.SendFinAsync(streamId, ct);
          }
          break;
        }

        case StreamTypes.Playback:
        {
          var sink = new TunnelStreamSink(muxer, streamId);
          try
          {
            await PlaybackHandler.RunAsync(reader, sink, _tapRegistry, _plugins,
              _loggerFactory.CreateLogger("PlaybackStream"), ct);
          }
          finally
          {
            sink.Close();
            await muxer.SendFinAsync(streamId, ct);
          }
          break;
        }

        case StreamTypes.EventChannel:
          try
          {
            await EventChannelHandler.RunAsync(reader,
              (flags, payload, c) => muxer.SendAsync(streamId, flags, payload, c),
              _eventBus,
              _loggerFactory.CreateLogger("EventChannel"), ct);
          }
          finally
          {
            await muxer.SendFinAsync(streamId, ct);
          }
          break;

        default:
          _logger.LogDebug("Unknown stream type 0x{Type:X4}, closing stream {StreamId}",
            streamType, streamId);
          await muxer.SendFinAsync(streamId, ct);
          break;
      }
    }
    catch (OperationCanceledException) { }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error handling stream {StreamId} type 0x{Type:X4}", streamId, streamType);
    }
  }

  private bool ValidateClientCert(
    object sender,
    X509Certificate? certificate,
    X509Chain? chain,
    SslPolicyErrors sslPolicyErrors)
  {
    if (certificate == null)
    {
      _logger.LogDebug("Client cert validation: no certificate");
      return false;
    }

    using var cert = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
    using var customChain = new X509Chain();
    customChain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
    customChain.ChainPolicy.CustomTrustStore.Add(_certs.RootCa);
    customChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

    var result = customChain.Build(cert);
    if (!result)
    {
      _logger.LogDebug("Client cert validation failed for {Subject}: {Status}",
        cert.Subject,
        string.Join(", ", customChain.ChainStatus.Select(s => s.StatusInformation)));
    }
    return result;
  }
}
