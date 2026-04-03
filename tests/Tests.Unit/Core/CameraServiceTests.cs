using Server.Core.Services;

namespace Tests.Unit.Core;

[TestFixture]
public class CameraServiceTests
{
  /// <summary>
  /// SCENARIO:
  /// Various shorthand and full ONVIF address formats
  ///
  /// ACTION:
  /// Normalize each address
  ///
  /// EXPECTED RESULT:
  /// Each is expanded to the correct full URL
  /// </summary>
  [TestCase("192.168.31.12", "http://192.168.31.12/onvif/device_service")]
  [TestCase("http://192.168.31.12", "http://192.168.31.12/onvif/device_service")]
  [TestCase("192.168.31.12:1234", "http://192.168.31.12:1234/onvif/device_service")]
  [TestCase("http://192.168.31.12:1234", "http://192.168.31.12:1234/onvif/device_service")]
  [TestCase("192.168.31.12/custom/path", "http://192.168.31.12/custom/path")]
  [TestCase("http://192.168.31.12:1234/custom/path", "http://192.168.31.12:1234/custom/path")]
  [TestCase("http://192.168.31.12/onvif/device_service", "http://192.168.31.12/onvif/device_service")]
  public void NormalizeOnvifAddress_ExpandsCorrectly(string input, string expected)
  {
    var result = CameraService.NormalizeOnvifAddress(input);

    Assert.That(new Uri(result).AbsoluteUri, Is.EqualTo(new Uri(expected).AbsoluteUri));
  }

  /// <summary>
  /// SCENARIO:
  /// Address with trailing slash and no path
  ///
  /// ACTION:
  /// Normalize the address
  ///
  /// EXPECTED RESULT:
  /// Trailing slash is preserved (user explicitly typed it, no default path added)
  /// </summary>
  [Test]
  public void NormalizeOnvifAddress_TrailingSlash_PreservesAsIs()
  {
    var result = CameraService.NormalizeOnvifAddress("192.168.31.12/");

    Assert.That(result, Does.Not.Contain("/onvif/device_service"));
  }

  /// <summary>
  /// SCENARIO:
  /// A standard RTSP URI with default port 554
  ///
  /// ACTION:
  /// Rewrite the port to 8554
  ///
  /// EXPECTED RESULT:
  /// The URI has port 8554, path and credentials preserved
  /// </summary>
  [Test]
  public void RewriteRtspPort_StandardUri_RewritesPort()
  {
    var uri = "rtsp://192.168.1.100:554/stream1";

    var result = CameraService.RewriteRtspPort(uri, 8554);

    Assert.That(result, Does.Contain(":8554/"));
    Assert.That(result, Does.Contain("/stream1"));
  }

  /// <summary>
  /// SCENARIO:
  /// An RTSP URI with embedded credentials
  ///
  /// ACTION:
  /// Rewrite the port to 9554
  ///
  /// EXPECTED RESULT:
  /// Port is rewritten, credentials are preserved
  /// </summary>
  [Test]
  public void RewriteRtspPort_UriWithCredentials_PreservesCredentials()
  {
    var uri = "rtsp://admin:password@192.168.1.100:554/cam/realmonitor";

    var result = CameraService.RewriteRtspPort(uri, 9554);

    Assert.That(result, Does.Contain(":9554/"));
    Assert.That(result, Does.Contain("admin:password"));
  }

  /// <summary>
  /// SCENARIO:
  /// An invalid/unparseable URI string
  ///
  /// ACTION:
  /// Attempt to rewrite the port
  ///
  /// EXPECTED RESULT:
  /// The original string is returned unchanged
  /// </summary>
  [Test]
  public void RewriteRtspPort_InvalidUri_ReturnsOriginal()
  {
    var uri = "not-a-valid-uri";

    var result = CameraService.RewriteRtspPort(uri, 8554);

    Assert.That(result, Is.EqualTo(uri));
  }

  /// <summary>
  /// SCENARIO:
  /// An RTSP URI with no explicit port
  ///
  /// ACTION:
  /// Rewrite the port to 7554
  ///
  /// EXPECTED RESULT:
  /// The port is added to the URI
  /// </summary>
  [Test]
  public void RewriteRtspPort_NoExplicitPort_AddsPort()
  {
    var uri = "rtsp://192.168.1.100/stream1";

    var result = CameraService.RewriteRtspPort(uri, 7554);

    Assert.That(result, Does.Contain(":7554/"));
  }
}
