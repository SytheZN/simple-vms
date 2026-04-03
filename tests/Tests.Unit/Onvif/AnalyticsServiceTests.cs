using Cameras.Onvif.Services;

namespace Tests.Unit.Onvif;

[TestFixture]
public class AnalyticsServiceTests
{
  /// <summary>
  /// SCENARIO:
  /// An AnalyticsModule with type containing "CellMotionDetector"
  ///
  /// ACTION:
  /// Check IsCellMotionDetector
  ///
  /// EXPECTED RESULT:
  /// Returns true
  /// </summary>
  [Test]
  public void IsCellMotionDetector_TypeContainsCellMotionDetector_ReturnsTrue()
  {
    var module = new AnalyticsModule
    {
      Name = "MyMotion",
      Type = "tt:CellMotionDetector",
      Rows = 18,
      Columns = 22
    };

    Assert.That(module.IsCellMotionDetector, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// An AnalyticsModule with name containing "CellMotionDetector"
  ///
  /// ACTION:
  /// Check IsCellMotionDetector
  ///
  /// EXPECTED RESULT:
  /// Returns true (matches on name as well as type)
  /// </summary>
  [Test]
  public void IsCellMotionDetector_NameContainsCellMotionDetector_ReturnsTrue()
  {
    var module = new AnalyticsModule
    {
      Name = "CellMotionDetector",
      Type = "some:vendor:type",
      Rows = 4,
      Columns = 4
    };

    Assert.That(module.IsCellMotionDetector, Is.True);
  }

  /// <summary>
  /// SCENARIO:
  /// An AnalyticsModule for a non-motion type
  ///
  /// ACTION:
  /// Check IsCellMotionDetector
  ///
  /// EXPECTED RESULT:
  /// Returns false
  /// </summary>
  [Test]
  public void IsCellMotionDetector_UnrelatedType_ReturnsFalse()
  {
    var module = new AnalyticsModule
    {
      Name = "FaceDetector",
      Type = "tt:FaceDetector",
      Rows = null,
      Columns = null
    };

    Assert.That(module.IsCellMotionDetector, Is.False);
  }

  /// <summary>
  /// SCENARIO:
  /// An AnalyticsModule with case-variant "cellmotiondetector" in type
  ///
  /// ACTION:
  /// Check IsCellMotionDetector
  ///
  /// EXPECTED RESULT:
  /// Returns true (case-insensitive match)
  /// </summary>
  [Test]
  public void IsCellMotionDetector_CaseInsensitive_ReturnsTrue()
  {
    var module = new AnalyticsModule
    {
      Name = "motion1",
      Type = "tt:cellmotiondetector",
      Rows = 10,
      Columns = 10
    };

    Assert.That(module.IsCellMotionDetector, Is.True);
  }
}
