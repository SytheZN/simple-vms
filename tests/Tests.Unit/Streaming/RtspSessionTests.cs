using System.Net;
using System.Net.Sockets;
using System.Text;
using Capture.Rtsp;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Models.Events;

namespace Tests.Unit.Streaming;

[TestFixture]
public class RtspSessionTests
{
  /// <summary>
  /// SCENARIO:
  /// A camera answers DESCRIBE but rejects SETUP while the session probes its video track
  ///
  /// ACTION:
  /// Call EnsureTrackAsync
  ///
  /// EXPECTED RESULT:
  /// The probe fails and its connection to the camera is closed rather than left open for
  /// the finalizer
  /// </summary>
  [Test]
  public async Task EnsureTrackAsync_SetupRejected_ClosesProbeConnection()
  {
    await using var camera = new ScriptedRtspCamera { RejectedMethod = "SETUP" };
    await using var session = NewSession(camera);

    Assert.ThrowsAsync<InvalidOperationException>(
      () => session.EnsureTrackAsync("video", CancellationToken.None));

    await camera.WaitForClosedConnectionsAsync(1);
  }

  /// <summary>
  /// SCENARIO:
  /// A camera probes successfully, rejects PLAY on the first real connect, then recovers
  ///
  /// ACTION:
  /// Add demand while PLAY is rejected, then add demand again once the camera accepts it
  ///
  /// EXPECTED RESULT:
  /// The failed attempt leaves no demand behind: the second attempt connects and plays, and
  /// removing that single demand tears the connection down
  /// </summary>
  [Test]
  public async Task AddDemandAsync_AfterRejectedPlay_ConnectsOnTheNextAttempt()
  {
    await using var camera = new ScriptedRtspCamera();
    await using var session = NewSession(camera);
    await session.EnsureTrackAsync("video", CancellationToken.None);

    camera.RejectedMethod = "PLAY";
    Assert.ThrowsAsync<InvalidOperationException>(
      () => session.AddDemandAsync(CancellationToken.None));

    camera.RejectedMethod = null;
    var acceptedPlaysBefore = camera.AcceptedPlays;
    await session.AddDemandAsync(CancellationToken.None);

    Assert.That(camera.AcceptedPlays, Is.EqualTo(acceptedPlaysBefore + 1));
    Assert.That(session.Completed.IsCompleted, Is.False);

    await session.RemoveDemandAsync();

    await camera.WaitForClosedConnectionsAsync(3);
  }

  /// <summary>
  /// SCENARIO:
  /// A camera with a live RTSP connection is removed from the system, and another camera's
  /// session is still in use
  ///
  /// ACTION:
  /// Publish CameraRemoved for the first camera
  ///
  /// EXPECTED RESULT:
  /// The plugin forgets only that camera's session and closes its connection
  /// </summary>
  [Test]
  public async Task CameraRemoved_ForgetsThatCamerasSessionOnly()
  {
    await using var removedCamera = new ScriptedRtspCamera();
    await using var keptCamera = new ScriptedRtspCamera();
    var removedCameraId = Guid.NewGuid();
    var eventBus = new Server.Plugins.EventBus();
    var plugin = new RtspPlugin();
    plugin.Initialize(new PluginContext
    {
      Config = new EmptyConfig(),
      Environment = new TestEnvironment(),
      LoggerFactory = NullPluginLoggerFactory.Instance,
      EventBus = eventBus
    });
    await plugin.StartAsync(CancellationToken.None);

    var removed = await plugin.ConnectAsync(
      new CameraConnectionInfo { CameraId = removedCameraId, Uri = removedCamera.Uri },
      CancellationToken.None);
    var kept = await plugin.ConnectAsync(
      new CameraConnectionInfo { CameraId = Guid.NewGuid(), Uri = keptCamera.Uri },
      CancellationToken.None);
    Assert.That(removed.IsT0 && kept.IsT0, Is.True);
    Assert.That(plugin.SessionCount, Is.EqualTo(2));

    await eventBus.PublishAsync(new CameraRemoved
    {
      CameraId = removedCameraId,
      Name = "removed",
      Timestamp = 0
    }, CancellationToken.None);

    await removedCamera.WaitForClosedConnectionsAsync(2);
    Assert.That(plugin.SessionCount, Is.EqualTo(1));
    Assert.That(kept.AsT0.Completed.IsCompleted, Is.False);

    await plugin.StopAsync(CancellationToken.None);
    Assert.That(plugin.SessionCount, Is.EqualTo(0));
  }

  private static RtspSession NewSession(ScriptedRtspCamera camera) =>
    new(Guid.NewGuid(), camera.Uri, null, null, null, NullLogger.Instance);

  private sealed class EmptyConfig : IConfig
  {
    public string Get(string key, string defaultValue) => defaultValue;
    public void Set(string key, string value) { }
  }

  private sealed class TestEnvironment : IServerEnvironment
  {
    public string DataPath => Path.GetTempPath();
  }

  private sealed class ScriptedRtspCamera : IAsyncDisposable
  {
    private static readonly string Sdp = string.Join("\r\n",
      "v=0",
      "o=- 0 0 IN IP4 0.0.0.0",
      "s=Session",
      "m=video 0 RTP/AVP 96",
      "a=rtpmap:96 H264/90000",
      "a=fmtp:96 packetization-mode=1;sprop-parameter-sets=Z0IAKeKQFAe3,aM48gA==",
      "a=control:trackID=1",
      "");

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _acceptLoop;
    private int _closedConnections;
    private int _acceptedPlays;

    public volatile string? RejectedMethod;
    public int AcceptedPlays => Volatile.Read(ref _acceptedPlays);
    public string Uri { get; }

    public ScriptedRtspCamera()
    {
      _listener.Start();
      var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
      Uri = $"rtsp://127.0.0.1:{port}/stream";
      _acceptLoop = Task.Run(AcceptAsync);
    }

    public async Task WaitForClosedConnectionsAsync(int expected)
    {
      using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
      while (Volatile.Read(ref _closedConnections) < expected)
        await Task.Delay(10, timeout.Token);
    }

    private async Task AcceptAsync()
    {
      try
      {
        while (true)
        {
          var client = await _listener.AcceptTcpClientAsync(_cts.Token);
          _ = Task.Run(() => ServeAsync(client));
        }
      }
      catch (OperationCanceledException) { }
      catch (SocketException) { }
    }

    private async Task ServeAsync(TcpClient client)
    {
      try
      {
        using var _ = client;
        var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII);

        while (await ReadRequestAsync(reader) is { } request)
          await stream.WriteAsync(Encoding.ASCII.GetBytes(Respond(request)), _cts.Token);
      }
      catch (IOException) { }
      catch (OperationCanceledException) { }
      finally
      {
        Interlocked.Increment(ref _closedConnections);
      }
    }

    private async Task<(string Method, string CSeq)?> ReadRequestAsync(StreamReader reader)
    {
      var requestLine = await reader.ReadLineAsync(_cts.Token);
      if (requestLine == null) return null;

      var cseq = "0";
      while (await reader.ReadLineAsync(_cts.Token) is { Length: > 0 } header)
      {
        if (header.StartsWith("CSeq:", StringComparison.OrdinalIgnoreCase))
          cseq = header["CSeq:".Length..].Trim();
      }

      return (requestLine.Split(' ')[0], cseq);
    }

    private string Respond((string Method, string CSeq) request)
    {
      if (request.Method == RejectedMethod)
        return $"RTSP/1.0 500 Internal Server Error\r\nCSeq: {request.CSeq}\r\n\r\n";

      var response = new StringBuilder($"RTSP/1.0 200 OK\r\nCSeq: {request.CSeq}\r\n");
      switch (request.Method)
      {
        case "DESCRIBE":
          response.Append("Content-Type: application/sdp\r\n");
          response.Append($"Content-Length: {Encoding.ASCII.GetByteCount(Sdp)}\r\n\r\n");
          response.Append(Sdp);
          return response.ToString();
        case "SETUP":
          response.Append("Session: 12345678;timeout=60\r\n");
          response.Append("Transport: RTP/AVP/TCP;unicast;interleaved=0-1\r\n");
          break;
        case "PLAY":
          Interlocked.Increment(ref _acceptedPlays);
          break;
      }
      response.Append("\r\n");
      return response.ToString();
    }

    public async ValueTask DisposeAsync()
    {
      await _cts.CancelAsync();
      _listener.Stop();
      await _acceptLoop;
      _cts.Dispose();
    }
  }
}
