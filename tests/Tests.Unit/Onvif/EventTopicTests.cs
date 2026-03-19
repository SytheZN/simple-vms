using Cameras.Onvif.Services;

namespace Tests.Unit.Onvif;

[TestFixture]
public class EventTopicTests
{
  [TestCase("tns1:RuleEngine/CellMotionDetector/Motion", "motion")]
  [TestCase("tns1:VideoSource/MotionAlarm", "motion")]
  [TestCase("tns1:Device/Trigger/DigitalInput", "io")]
  [TestCase("tns1:Device/Trigger/Relay", "generic")]
  [TestCase("tns1:Device/HardwareFailure/StorageFailure", "storage")]
  [TestCase("tns1:VideoSource/GlobalSceneChange/ImagingService", "tamper")]
  [TestCase("tns1:AccessControl/AccessGranted", "access")]
  [TestCase("tns1:SomeVendor/CustomEvent", "generic")]
  public void TopicToEventType_MapsCorrectly(string topic, string expected)
  {
    var result = EventService.TopicToEventType(topic);

    Assert.That(result, Is.EqualTo(expected));
  }

  [Test]
  public void TopicToEventType_RelayOutput_MapsToIo()
  {
    var result = EventService.TopicToEventType("tns1:Device/Trigger/RelayOutput");

    Assert.That(result, Is.EqualTo("io"));
  }

  [Test]
  public void TopicToEventType_DoorControl_MapsToAccess()
  {
    var result = EventService.TopicToEventType("tns1:DoorControl/DoorPhysicalState");

    Assert.That(result, Is.EqualTo("access"));
  }

  [Test]
  public void TopicToEventType_StorageFull_MapsToStorage()
  {
    var result = EventService.TopicToEventType("tns1:Device/HardwareFailure/StorageFull");

    Assert.That(result, Is.EqualTo("storage"));
  }
}
