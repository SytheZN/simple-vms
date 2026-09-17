using System.Xml.Linq;
using Cameras.Onvif;
using Cameras.Onvif.Services;
using Cameras.Onvif.Soap;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests.Unit.Onvif;

[TestFixture]
public class OnvifEventSubscriptionTests
{
  private const int PullsBeforeStopping = 5;

  /// <summary>
  /// SCENARIO:
  /// A pull point is due for renewal and the camera answers the renew with a termination
  /// time ten minutes out
  ///
  /// ACTION:
  /// Read events across several pulls
  ///
  /// EXPECTED RESULT:
  /// The subscription renews once and then trusts the new termination time, instead of
  /// renewing again before every pull
  /// </summary>
  [Test]
  public async Task ReadEventsAsync_AfterRenew_DoesNotRenewAgainUntilDue()
  {
    var camera = new FakeCamera(renewReportsTerminationTime: true);

    await ReadUntilStoppedAsync(camera);

    Assert.That(camera.Pulls, Is.EqualTo(PullsBeforeStopping));
    Assert.That(camera.Renews, Is.EqualTo(1));
  }

  /// <summary>
  /// SCENARIO:
  /// A pull point is due for renewal and the camera's renew response carries no termination
  /// time
  ///
  /// ACTION:
  /// Read events across several pulls
  ///
  /// EXPECTED RESULT:
  /// The subscription assumes the termination time it asked for and still renews only once
  /// </summary>
  [Test]
  public async Task ReadEventsAsync_RenewWithoutTerminationTime_AssumesRequestedLifetime()
  {
    var camera = new FakeCamera(renewReportsTerminationTime: false);

    await ReadUntilStoppedAsync(camera);

    Assert.That(camera.Pulls, Is.EqualTo(PullsBeforeStopping));
    Assert.That(camera.Renews, Is.EqualTo(1));
  }

  private static async Task ReadUntilStoppedAsync(FakeCamera camera)
  {
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    camera.StopAfter(PullsBeforeStopping, cts);

    var subscription = new OnvifEventSubscription(
      new EventService(camera),
      "http://camera/events",
      "http://camera/pullpoint",
      new Credentials(),
      Guid.NewGuid(),
      terminationTime: DateTimeOffset.UtcNow,
      NullLogger.Instance);

    await foreach (var _ in subscription.ReadEventsAsync(cts.Token)) { }
  }

  private sealed class FakeCamera(bool renewReportsTerminationTime) : ISoapClient
  {
    private int _stopAfterPulls;
    private CancellationTokenSource? _stop;

    public int Pulls { get; private set; }
    public int Renews { get; private set; }

    public void StopAfter(int pulls, CancellationTokenSource stop)
    {
      _stopAfterPulls = pulls;
      _stop = stop;
    }

    public Task<XElement> SendAsync(
      string uri,
      XElement body,
      Credentials? credentials = null,
      CancellationToken ct = default,
      bool logFaults = true)
    {
      switch (body.Name.LocalName)
      {
        case "PullMessages":
          Pulls++;
          if (Pulls >= _stopAfterPulls)
            _stop!.Cancel();
          return Respond(new XElement(XmlHelpers.NsEvent + "PullMessagesResponse"));
        case "Renew":
          Renews++;
          return Respond(RenewResponse());
        default:
          throw new NotSupportedException(body.Name.LocalName);
      }
    }

    private XElement RenewResponse()
    {
      var response = new XElement(XmlHelpers.NsWsnt + "RenewResponse");
      if (renewReportsTerminationTime)
        response.Add(new XElement(XmlHelpers.NsWsnt + "TerminationTime",
          DateTimeOffset.UtcNow.AddMinutes(10).ToString("O")));
      return response;
    }

    private static Task<XElement> Respond(XElement content) =>
      Task.FromResult(new XElement("Body", content));
  }
}
