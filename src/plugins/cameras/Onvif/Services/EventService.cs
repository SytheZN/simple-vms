using System.Xml.Linq;
using Cameras.Onvif.Soap;
using Shared.Models;

namespace Cameras.Onvif.Services;

public sealed class EventService(SoapClient soap)
{
  private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

  public async Task<PullPointInfo> CreatePullPointAsync(
    string eventUri, Credentials credentials, CancellationToken ct)
  {
    var body = new XElement(XmlHelpers.NsEvent + "CreatePullPointSubscription",
      new XElement(XmlHelpers.NsEvent + "InitialTerminationTime", "PT600S"));
    var response = await soap.SendAsync(eventUri, body, credentials, ct);

    var result = response.Element(XmlHelpers.NsEvent + "CreatePullPointSubscriptionResponse");
    var reference = result
      ?.Element(XmlHelpers.NsEvent + "SubscriptionReference")
      ?.Element(XmlHelpers.NsWsa + "Address")?.Value
      ?? throw new SoapFaultException("No subscription reference in response");

    return new PullPointInfo
    {
      SubscriptionUri = reference,
      TerminationTime = ParseTerminationTime(
        result?.Element(XmlHelpers.NsWsnt + "TerminationTime")?.Value)
    };
  }

  public async Task<IReadOnlyList<OnvifNotification>> PullMessagesAsync(
    string pullPointUri, Credentials credentials, CancellationToken ct)
  {
    var body = new XElement(XmlHelpers.NsEvent + "PullMessages",
      new XElement(XmlHelpers.NsEvent + "Timeout", $"PT{(int)DefaultTimeout.TotalSeconds}S"),
      new XElement(XmlHelpers.NsEvent + "MessageLimit", 100));
    var response = await soap.SendAsync(pullPointUri, body, credentials, ct);

    var result = response.Element(XmlHelpers.NsEvent + "PullMessagesResponse");
    if (result == null) return [];

    var notifications = new List<OnvifNotification>();
    foreach (var msg in result.Elements(XmlHelpers.NsWsnt + "NotificationMessage"))
    {
      var topic = msg.Element(XmlHelpers.NsWsnt + "Topic")?.Value;
      if (string.IsNullOrEmpty(topic)) continue;

      var message = msg
        .Element(XmlHelpers.NsWsnt + "Message")
        ?.Element(XmlHelpers.NsSchema + "Message");

      var data = new Dictionary<string, string>();
      var dataEl = message?.Element(XmlHelpers.NsSchema + "Data");
      if (dataEl != null)
      {
        foreach (var item in dataEl.Elements(XmlHelpers.NsSchema + "SimpleItem"))
        {
          var name = item.Attribute("Name")?.Value;
          var value = item.Attribute("Value")?.Value;
          if (name != null && value != null)
            data[name] = value;
        }
      }

      var sourceData = new Dictionary<string, string>();
      var sourceEl = message?.Element(XmlHelpers.NsSchema + "Source");
      if (sourceEl != null)
      {
        foreach (var item in sourceEl.Elements(XmlHelpers.NsSchema + "SimpleItem"))
        {
          var name = item.Attribute("Name")?.Value;
          var value = item.Attribute("Value")?.Value;
          if (name != null && value != null)
            sourceData[name] = value;
        }
      }

      notifications.Add(new OnvifNotification
      {
        Topic = topic,
        EventType = TopicToEventType(topic),
        IsActive = data.TryGetValue("State", out var state)
          ? string.Equals(state, "true", StringComparison.OrdinalIgnoreCase)
          : data.TryGetValue("IsMotion", out var motion)
            ? string.Equals(motion, "true", StringComparison.OrdinalIgnoreCase)
            : null,
        Data = data.Count > 0 ? data : null,
        Source = sourceData.Count > 0 ? sourceData : null,
        Timestamp = ParseUtcTime(
          message?.Attribute("UtcTime")?.Value)
      });
    }

    return notifications;
  }

  public async Task RenewAsync(
    string subscriptionUri, Credentials credentials, CancellationToken ct)
  {
    var body = new XElement(XmlHelpers.NsWsnt + "Renew",
      new XElement(XmlHelpers.NsWsnt + "TerminationTime", "PT600S"));
    await soap.SendAsync(subscriptionUri, body, credentials, ct);
  }

  public async Task UnsubscribeAsync(
    string subscriptionUri, Credentials credentials, CancellationToken ct)
  {
    var body = new XElement(XmlHelpers.NsWsnt + "Unsubscribe");
    await soap.SendAsync(subscriptionUri, body, credentials, ct);
  }

  public static string TopicToEventType(string topic)
  {
    var normalized = topic
      .Replace("tns1:", "")
      .Replace("tns2:", "")
      .Replace("tt:", "");

    if (normalized.Contains("MotionAlarm", StringComparison.OrdinalIgnoreCase)
      || normalized.Contains("CellMotionDetector", StringComparison.OrdinalIgnoreCase)
      || normalized.Contains("Motion", StringComparison.OrdinalIgnoreCase))
      return "motion";

    if (normalized.Contains("Tamper", StringComparison.OrdinalIgnoreCase)
      || normalized.Contains("GlobalSceneChange", StringComparison.OrdinalIgnoreCase))
      return "tamper";

    if (normalized.Contains("DigitalInput", StringComparison.OrdinalIgnoreCase)
      || normalized.Contains("RelayOutput", StringComparison.OrdinalIgnoreCase))
      return "io";

    if (normalized.Contains("DoorControl", StringComparison.OrdinalIgnoreCase)
      || normalized.Contains("AccessControl", StringComparison.OrdinalIgnoreCase))
      return "access";

    if (normalized.Contains("StorageFailure", StringComparison.OrdinalIgnoreCase)
      || normalized.Contains("StorageFull", StringComparison.OrdinalIgnoreCase))
      return "storage";

    return "generic";
  }

  private static DateTimeOffset? ParseUtcTime(string? value) =>
    DateTimeOffset.TryParse(value, out var dt) ? dt : null;

  private static DateTimeOffset ParseTerminationTime(string? value) =>
    DateTimeOffset.TryParse(value, out var dt) ? dt : DateTimeOffset.UtcNow.AddMinutes(10);
}

public sealed class PullPointInfo
{
  public required string SubscriptionUri { get; init; }
  public required DateTimeOffset TerminationTime { get; init; }
}

public sealed class OnvifNotification
{
  public required string Topic { get; init; }
  public required string EventType { get; init; }
  public bool? IsActive { get; init; }
  public Dictionary<string, string>? Data { get; init; }
  public Dictionary<string, string>? Source { get; init; }
  public DateTimeOffset? Timestamp { get; init; }
}
