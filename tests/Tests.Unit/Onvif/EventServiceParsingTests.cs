using System.Net;
using System.Xml.Linq;
using Cameras.Onvif.Services;
using Cameras.Onvif.Soap;
using Shared.Models;

namespace Tests.Unit.Onvif;

[TestFixture]
public class EventServiceParsingTests
{
  /// <summary>
  /// SCENARIO:
  /// PullMessages response contains a motion notification with data and source
  ///
  /// ACTION:
  /// Call PullMessagesAsync
  ///
  /// EXPECTED RESULT:
  /// Returns parsed notification with topic, event type, data, and source
  /// </summary>
  [Test]
  public async Task PullMessages_ParsesNotificationWithDataAndSource()
  {
    var responseXml = BuildSoapResponse(new XElement(XmlHelpers.NsEvent + "PullMessagesResponse",
      new XElement(XmlHelpers.NsWsnt + "NotificationMessage",
        new XElement(XmlHelpers.NsWsnt + "Topic", "tns1:RuleEngine/CellMotionDetector/Motion"),
        new XElement(XmlHelpers.NsWsnt + "Message",
          new XElement(XmlHelpers.NsSchema + "Message",
            new XAttribute("UtcTime", "2026-03-20T10:00:00Z"),
            new XElement(XmlHelpers.NsSchema + "Source",
              new XElement(XmlHelpers.NsSchema + "SimpleItem",
                new XAttribute("Name", "VideoSourceToken"),
                new XAttribute("Value", "src0"))),
            new XElement(XmlHelpers.NsSchema + "Data",
              new XElement(XmlHelpers.NsSchema + "SimpleItem",
                new XAttribute("Name", "State"),
                new XAttribute("Value", "true"))))))));

    var service = CreateService(responseXml);
    var notifications = await service.PullMessagesAsync(
      "http://cam/pullpoint", Credentials.FromUserPass("admin", ""),
      CancellationToken.None);

    Assert.That(notifications, Has.Count.EqualTo(1));
    Assert.That(notifications[0].Topic, Is.EqualTo("tns1:RuleEngine/CellMotionDetector/Motion"));
    Assert.That(notifications[0].EventType, Is.EqualTo("motion"));
    Assert.That(notifications[0].IsActive, Is.True);
    Assert.That(notifications[0].Data!["State"], Is.EqualTo("true"));
    Assert.That(notifications[0].Source!["VideoSourceToken"], Is.EqualTo("src0"));
    Assert.That(notifications[0].Timestamp, Is.Not.Null);
  }

  /// <summary>
  /// SCENARIO:
  /// PullMessages response is empty (no notifications)
  ///
  /// ACTION:
  /// Call PullMessagesAsync
  ///
  /// EXPECTED RESULT:
  /// Returns empty list
  /// </summary>
  [Test]
  public async Task PullMessages_EmptyResponse_ReturnsEmpty()
  {
    var responseXml = BuildSoapResponse(
      new XElement(XmlHelpers.NsEvent + "PullMessagesResponse"));

    var service = CreateService(responseXml);
    var notifications = await service.PullMessagesAsync(
      "http://cam/pullpoint", Credentials.FromUserPass("admin", ""),
      CancellationToken.None);

    Assert.That(notifications, Is.Empty);
  }

  /// <summary>
  /// SCENARIO:
  /// PullMessages response has notification with IsMotion instead of State
  ///
  /// ACTION:
  /// Call PullMessagesAsync
  ///
  /// EXPECTED RESULT:
  /// IsActive is parsed from IsMotion field
  /// </summary>
  [Test]
  public async Task PullMessages_IsMotionField_ParsesAsActive()
  {
    var responseXml = BuildSoapResponse(new XElement(XmlHelpers.NsEvent + "PullMessagesResponse",
      new XElement(XmlHelpers.NsWsnt + "NotificationMessage",
        new XElement(XmlHelpers.NsWsnt + "Topic", "tns1:VideoSource/MotionAlarm"),
        new XElement(XmlHelpers.NsWsnt + "Message",
          new XElement(XmlHelpers.NsSchema + "Message",
            new XElement(XmlHelpers.NsSchema + "Data",
              new XElement(XmlHelpers.NsSchema + "SimpleItem",
                new XAttribute("Name", "IsMotion"),
                new XAttribute("Value", "false"))))))));

    var service = CreateService(responseXml);
    var notifications = await service.PullMessagesAsync(
      "http://cam/pullpoint", Credentials.FromUserPass("admin", ""),
      CancellationToken.None);

    Assert.That(notifications, Has.Count.EqualTo(1));
    Assert.That(notifications[0].IsActive, Is.False);
  }

  /// <summary>
  /// SCENARIO:
  /// PullMessages response has notification without data
  ///
  /// ACTION:
  /// Call PullMessagesAsync
  ///
  /// EXPECTED RESULT:
  /// IsActive is null, Data is null
  /// </summary>
  [Test]
  public async Task PullMessages_NoData_IsActiveNull()
  {
    var responseXml = BuildSoapResponse(new XElement(XmlHelpers.NsEvent + "PullMessagesResponse",
      new XElement(XmlHelpers.NsWsnt + "NotificationMessage",
        new XElement(XmlHelpers.NsWsnt + "Topic", "tns1:Device/Trigger/DigitalInput"),
        new XElement(XmlHelpers.NsWsnt + "Message",
          new XElement(XmlHelpers.NsSchema + "Message")))));

    var service = CreateService(responseXml);
    var notifications = await service.PullMessagesAsync(
      "http://cam/pullpoint", Credentials.FromUserPass("admin", ""),
      CancellationToken.None);

    Assert.That(notifications, Has.Count.EqualTo(1));
    Assert.That(notifications[0].IsActive, Is.Null);
    Assert.That(notifications[0].Data, Is.Null);
    Assert.That(notifications[0].Source, Is.Null);
  }

  /// <summary>
  /// SCENARIO:
  /// CreatePullPointSubscription response with subscription reference
  ///
  /// ACTION:
  /// Call CreatePullPointAsync
  ///
  /// EXPECTED RESULT:
  /// Returns PullPointInfo with subscription URI and termination time
  /// </summary>
  [Test]
  public async Task CreatePullPoint_ParsesSubscriptionUri()
  {
    var responseXml = BuildSoapResponse(new XElement(XmlHelpers.NsEvent + "CreatePullPointSubscriptionResponse",
      new XElement(XmlHelpers.NsEvent + "SubscriptionReference",
        new XElement(XmlHelpers.NsWsa + "Address", "http://cam/pullpoint/sub1")),
      new XElement(XmlHelpers.NsWsnt + "TerminationTime", "2026-03-20T10:10:00Z")));

    var service = CreateService(responseXml);
    var info = await service.CreatePullPointAsync(
      "http://cam/events", Credentials.FromUserPass("admin", ""),
      CancellationToken.None);

    Assert.That(info.SubscriptionUri, Is.EqualTo("http://cam/pullpoint/sub1"));
    Assert.That(info.TerminationTime.Year, Is.EqualTo(2026));
  }

  private static EventService CreateService(string responseXml)
  {
    var handler = new FakeHttpHandler(responseXml);
    var http = new HttpClient(handler);
    return new EventService(new SoapClient(http));
  }

  private static string BuildSoapResponse(XElement body)
  {
    return XmlHelpers.BuildEnvelope(body).ToString();
  }

  private sealed class FakeHttpHandler(string responseXml) : HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken ct)
    {
      return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
      {
        Content = new StringContent(responseXml, System.Text.Encoding.UTF8, "application/soap+xml")
      });
    }
  }
}
