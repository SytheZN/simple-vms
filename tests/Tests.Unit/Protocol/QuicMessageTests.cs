using MessagePack;
using Shared.Protocol;

namespace Tests.Unit.Protocol;

[TestFixture]
public class QuicMessageTests
{
  private static readonly MessagePackSerializerOptions Options = ProtocolSerializer.Options;

  private static T RoundTrip<T>(T value)
  {
    var bytes = MessagePackSerializer.Serialize(value, Options);
    return MessagePackSerializer.Deserialize<T>(bytes, Options);
  }

  /// <summary>
  /// SCENARIO:
  /// Serialize and deserialize a KeepaliveMessage
  ///
  /// ACTION:
  /// Round-trip through MessagePack
  ///
  /// EXPECTED RESULT:
  /// Echo value is preserved
  /// </summary>
  [Test]
  public void KeepaliveMessage_RoundTrips()
  {
    var msg = new KeepaliveMessage { Echo = 0xDEADBEEFCAFEBABE };
    var result = RoundTrip(msg);
    Assert.That(result.Echo, Is.EqualTo(0xDEADBEEFCAFEBABE));
  }

  /// <summary>
  /// SCENARIO:
  /// Serialize and deserialize an ApiRequestMessage with body
  ///
  /// ACTION:
  /// Round-trip through MessagePack
  ///
  /// EXPECTED RESULT:
  /// Method, Path, and Body are preserved
  /// </summary>
  [Test]
  public void ApiRequestMessage_WithBody_RoundTrips()
  {
    var msg = new ApiRequestMessage
    {
      Method = "POST",
      Path = "/api/v1/cameras",
      Body = [0x01, 0x02, 0x03]
    };

    var result = RoundTrip(msg);

    Assert.That(result.Method, Is.EqualTo("POST"));
    Assert.That(result.Path, Is.EqualTo("/api/v1/cameras"));
    Assert.That(result.Body, Is.EqualTo(new byte[] { 0x01, 0x02, 0x03 }));
  }

  /// <summary>
  /// SCENARIO:
  /// Serialize and deserialize an ApiRequestMessage without body
  ///
  /// ACTION:
  /// Round-trip through MessagePack
  ///
  /// EXPECTED RESULT:
  /// Body is null
  /// </summary>
  [Test]
  public void ApiRequestMessage_NullBody_RoundTrips()
  {
    var msg = new ApiRequestMessage
    {
      Method = "GET",
      Path = "/api/v1/cameras"
    };

    var result = RoundTrip(msg);

    Assert.That(result.Method, Is.EqualTo("GET"));
    Assert.That(result.Path, Is.EqualTo("/api/v1/cameras"));
    Assert.That(result.Body, Is.Null);
  }

  /// <summary>
  /// SCENARIO:
  /// Serialize and deserialize an ApiResponseMessage with all fields
  ///
  /// ACTION:
  /// Round-trip through MessagePack
  ///
  /// EXPECTED RESULT:
  /// Result, DebugTag, Message, and Body are preserved
  /// </summary>
  [Test]
  public void ApiResponseMessage_RoundTrips()
  {
    var msg = new ApiResponseMessage
    {
      Result = 0,
      DebugTag = 0x00010010,
      Message = null,
      Body = [0xAA]
    };

    var result = RoundTrip(msg);

    Assert.That(result.Result, Is.EqualTo(0));
    Assert.That(result.DebugTag, Is.EqualTo(0x00010010u));
    Assert.That(result.Message, Is.Null);
    Assert.That(result.Body, Is.EqualTo(new byte[] { 0xAA }));
  }

  /// <summary>
  /// SCENARIO:
  /// Serialize and deserialize an ApiResponseMessage with non-null message
  ///
  /// ACTION:
  /// Round-trip through MessagePack
  ///
  /// EXPECTED RESULT:
  /// Message string is preserved
  /// </summary>
  [Test]
  public void ApiResponseMessage_WithMessage_RoundTrips()
  {
    var msg = new ApiResponseMessage
    {
      Result = 4,
      DebugTag = 0x00030001,
      Message = "Camera not found",
      Body = null
    };

    var result = RoundTrip(msg);

    Assert.That(result.Result, Is.EqualTo(4));
    Assert.That(result.DebugTag, Is.EqualTo(0x00030001u));
    Assert.That(result.Message, Is.EqualTo("Camera not found"));
    Assert.That(result.Body, Is.Null);
  }

  /// <summary>
  /// SCENARIO:
  /// Serialize and deserialize a LiveSubscribeMessage
  ///
  /// ACTION:
  /// Round-trip through MessagePack
  ///
  /// EXPECTED RESULT:
  /// CameraId and Profile are preserved
  /// </summary>
  [Test]
  public void LiveSubscribeMessage_RoundTrips()
  {
    var id = Guid.NewGuid();
    var msg = new LiveSubscribeMessage { CameraId = id, Profile = "main" };

    var result = RoundTrip(msg);

    Assert.That(result.CameraId, Is.EqualTo(id));
    Assert.That(result.Profile, Is.EqualTo("main"));
  }

  /// <summary>
  /// SCENARIO:
  /// Serialize and deserialize a PlaybackRequestMessage with end timestamp
  ///
  /// ACTION:
  /// Round-trip through MessagePack
  ///
  /// EXPECTED RESULT:
  /// All fields including nullable To are preserved
  /// </summary>
  [Test]
  public void PlaybackRequestMessage_WithEnd_RoundTrips()
  {
    var id = Guid.NewGuid();
    var msg = new PlaybackRequestMessage
    {
      CameraId = id,
      Profile = "sub",
      From = 1000000UL,
      To = 2000000UL
    };

    var result = RoundTrip(msg);

    Assert.That(result.CameraId, Is.EqualTo(id));
    Assert.That(result.Profile, Is.EqualTo("sub"));
    Assert.That(result.From, Is.EqualTo(1000000UL));
    Assert.That(result.To, Is.EqualTo(2000000UL));
  }

  /// <summary>
  /// SCENARIO:
  /// Serialize and deserialize a PlaybackRequestMessage without end timestamp
  ///
  /// ACTION:
  /// Round-trip through MessagePack
  ///
  /// EXPECTED RESULT:
  /// To is null (open-ended playback)
  /// </summary>
  [Test]
  public void PlaybackRequestMessage_OpenEnded_RoundTrips()
  {
    var msg = new PlaybackRequestMessage
    {
      CameraId = Guid.NewGuid(),
      Profile = "main",
      From = 1000000UL,
      To = null
    };

    var result = RoundTrip(msg);

    Assert.That(result.To, Is.Null);
  }

  /// <summary>
  /// SCENARIO:
  /// Serialize and deserialize a FragmentMessage
  ///
  /// ACTION:
  /// Round-trip through MessagePack
  ///
  /// EXPECTED RESULT:
  /// Timestamp and Data are preserved
  /// </summary>
  [Test]
  public void FragmentMessage_RoundTrips()
  {
    var data = new byte[1024];
    Random.Shared.NextBytes(data);
    var msg = new FragmentMessage { Timestamp = 9999999UL, Data = data };

    var result = RoundTrip(msg);

    Assert.That(result.Timestamp, Is.EqualTo(9999999UL));
    Assert.That(result.Data, Is.EqualTo(data));
  }

  /// <summary>
  /// SCENARIO:
  /// Serialize and deserialize an EventChannelMessage with all fields
  ///
  /// ACTION:
  /// Round-trip through MessagePack
  ///
  /// EXPECTED RESULT:
  /// All fields are preserved including nullable EndTime
  /// </summary>
  [Test]
  public void EventChannelMessage_RoundTrips()
  {
    var id = Guid.NewGuid();
    var camId = Guid.NewGuid();
    var msg = new EventChannelMessage
    {
      Id = id,
      CameraId = camId,
      Type = "motion",
      StartTime = 5000000UL,
      EndTime = 6000000UL,
      Metadata = null
    };

    var result = RoundTrip(msg);

    Assert.That(result.Id, Is.EqualTo(id));
    Assert.That(result.CameraId, Is.EqualTo(camId));
    Assert.That(result.Type, Is.EqualTo("motion"));
    Assert.That(result.StartTime, Is.EqualTo(5000000UL));
    Assert.That(result.EndTime, Is.EqualTo(6000000UL));
    Assert.That(result.Metadata, Is.Null);
  }

  /// <summary>
  /// SCENARIO:
  /// Serialize and deserialize an EventChannelMessage with null EndTime
  ///
  /// ACTION:
  /// Round-trip through MessagePack
  ///
  /// EXPECTED RESULT:
  /// EndTime is null (instantaneous event)
  /// </summary>
  [Test]
  public void EventChannelMessage_NullEndTime_RoundTrips()
  {
    var msg = new EventChannelMessage
    {
      Id = Guid.NewGuid(),
      CameraId = Guid.NewGuid(),
      Type = "tamper",
      StartTime = 5000000UL,
      EndTime = null
    };

    var result = RoundTrip(msg);

    Assert.That(result.EndTime, Is.Null);
  }

  /// <summary>
  /// SCENARIO:
  /// Serialize and deserialize an EventChannelMessage with metadata
  ///
  /// ACTION:
  /// Round-trip through MessagePack with a metadata dictionary
  ///
  /// EXPECTED RESULT:
  /// Metadata keys and string values are preserved
  /// </summary>
  [Test]
  public void EventChannelMessage_WithMetadata_RoundTrips()
  {
    var msg = new EventChannelMessage
    {
      Id = Guid.NewGuid(),
      CameraId = Guid.NewGuid(),
      Type = "motion",
      StartTime = 5000000UL,
      EndTime = 6000000UL,
      Metadata = new Dictionary<string, string>
      {
        ["zone"] = "front_door",
        ["confidence"] = "0.95"
      }
    };

    var result = RoundTrip(msg);

    Assert.That(result.Metadata, Is.Not.Null);
    Assert.That(result.Metadata!["zone"], Is.EqualTo("front_door"));
    Assert.That(result.Metadata["confidence"], Is.EqualTo("0.95"));
  }

  /// <summary>
  /// SCENARIO:
  /// Serialize and deserialize a StreamErrorMessage
  ///
  /// ACTION:
  /// Round-trip through MessagePack
  ///
  /// EXPECTED RESULT:
  /// Result, DebugTag, and Message are preserved
  /// </summary>
  [Test]
  public void StreamErrorMessage_RoundTrips()
  {
    var msg = new StreamErrorMessage
    {
      Result = 4,
      DebugTag = 0x000C0001,
      Message = "No pipeline for camera"
    };

    var result = RoundTrip(msg);

    Assert.That(result.Result, Is.EqualTo(4));
    Assert.That(result.DebugTag, Is.EqualTo(0x000C0001u));
    Assert.That(result.Message, Is.EqualTo("No pipeline for camera"));
  }
}
