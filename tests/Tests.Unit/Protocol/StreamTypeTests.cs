using Shared.Protocol;

namespace Tests.Unit.Protocol;

[TestFixture]
public class StreamTypeTests
{
  /// <summary>
  /// SCENARIO:
  /// Each defined stream type constant
  ///
  /// ACTION:
  /// Check IsValid
  ///
  /// EXPECTED RESULT:
  /// All defined types are valid
  /// </summary>
  [TestCase(StreamTypes.Keepalive)]
  [TestCase(StreamTypes.ApiRequest)]
  [TestCase(StreamTypes.LiveSubscribe)]
  [TestCase(StreamTypes.Playback)]
  [TestCase(StreamTypes.EventChannel)]
  public void DefinedTypes_AreValid(ushort type)
  {
    Assert.That(StreamTypes.IsValid(type), Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// Stream type 0x0000 (reserved) and values outside defined ranges
  ///
  /// ACTION:
  /// Check IsValid
  ///
  /// EXPECTED RESULT:
  /// Invalid types are rejected
  /// </summary>
  [TestCase((ushort)0x0000)]
  [TestCase((ushort)0x0050)]
  [TestCase((ushort)0x0500)]
  [TestCase((ushort)0x0FFF)]
  [TestCase((ushort)0x2000)]
  [TestCase((ushort)0xFFFF)]
  public void InvalidTypes_AreRejected(ushort type)
  {
    Assert.That(StreamTypes.IsValid(type), Is.False);
  }

  /// <summary>
  /// SCENARIO:
  /// Plugin range stream types
  ///
  /// ACTION:
  /// Check IsPlugin and IsValid
  ///
  /// EXPECTED RESULT:
  /// Plugin range types are both valid and identified as plugin types
  /// </summary>
  [TestCase((ushort)0x1000)]
  [TestCase((ushort)0x1500)]
  [TestCase((ushort)0x1FFF)]
  public void PluginRange_IsPluginAndValid(ushort type)
  {
    Assert.That(StreamTypes.IsValid(type), Is.True);
    Assert.That(StreamTypes.IsPlugin(type), Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// Core stream types are not in the plugin range
  ///
  /// ACTION:
  /// Check IsPlugin for core types
  ///
  /// EXPECTED RESULT:
  /// Core types are not plugins
  /// </summary>
  [TestCase(StreamTypes.Keepalive)]
  [TestCase(StreamTypes.ApiRequest)]
  [TestCase(StreamTypes.LiveSubscribe)]
  public void CoreTypes_AreNotPlugin(ushort type)
  {
    Assert.That(StreamTypes.IsPlugin(type), Is.False);
  }

  /// <summary>
  /// SCENARIO:
  /// All defined stream type constants
  ///
  /// ACTION:
  /// Collect all constants and check for duplicates
  ///
  /// EXPECTED RESULT:
  /// No two constants share the same value
  /// </summary>
  [Test]
  public void DefinedTypes_NoDuplicates()
  {
    var types = new ushort[]
    {
      StreamTypes.Keepalive,
      StreamTypes.ApiRequest,
      StreamTypes.LiveSubscribe,
      StreamTypes.Playback,
      StreamTypes.EventChannel
    };

    Assert.That(types.Distinct().Count(), Is.EqualTo(types.Length));
  }

  /// <summary>
  /// SCENARIO:
  /// Each defined stream type falls within its documented range
  ///
  /// ACTION:
  /// Verify each type against its expected range
  ///
  /// EXPECTED RESULT:
  /// Types are in the correct category ranges
  /// </summary>
  [Test]
  public void DefinedTypes_InCorrectRanges()
  {
    Assert.That(StreamTypes.Keepalive, Is.InRange(StreamTypes.ControlRangeStart, StreamTypes.ControlRangeEnd));
    Assert.That(StreamTypes.ApiRequest, Is.InRange(StreamTypes.ApiRangeStart, StreamTypes.ApiRangeEnd));
    Assert.That(StreamTypes.LiveSubscribe, Is.InRange(StreamTypes.VideoRangeStart, StreamTypes.VideoRangeEnd));
    Assert.That(StreamTypes.Playback, Is.InRange(StreamTypes.VideoRangeStart, StreamTypes.VideoRangeEnd));
    Assert.That(StreamTypes.EventChannel, Is.InRange(StreamTypes.EventRangeStart, StreamTypes.EventRangeEnd));
  }
}
