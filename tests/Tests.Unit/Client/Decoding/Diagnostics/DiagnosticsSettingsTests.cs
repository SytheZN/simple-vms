using System.ComponentModel;
using Client.Core.Decoding.Diagnostics;

namespace Tests.Unit.Client.Decoding.Diagnostics;

[TestFixture]
public class DiagnosticsSettingsTests
{
  /// <summary>
  /// SCENARIO:
  /// A fresh DiagnosticsSettings starts with the overlay hidden
  ///
  /// ACTION:
  /// Read ShowOverlay immediately after construction
  ///
  /// EXPECTED RESULT:
  /// ShowOverlay is false
  /// </summary>
  [Test]
  public void Default_ShowOverlay_IsFalse()
  {
    Assert.That(new DiagnosticsSettings().ShowOverlay, Is.False);
  }

  /// <summary>
  /// SCENARIO:
  /// ShowOverlay is set to true from its default false value
  ///
  /// ACTION:
  /// Subscribe to PropertyChanged, set ShowOverlay = true
  ///
  /// EXPECTED RESULT:
  /// PropertyChanged fires once with the ShowOverlay name; the new value is observable
  /// </summary>
  [Test]
  public void SetShowOverlay_ChangesValue_FiresPropertyChanged()
  {
    var settings = new DiagnosticsSettings();
    var events = new List<string?>();
    settings.PropertyChanged += (_, e) => events.Add(e.PropertyName);

    settings.ShowOverlay = true;

    Assert.Multiple(() =>
    {
      Assert.That(settings.ShowOverlay, Is.True);
      Assert.That(events, Is.EqualTo(new[] { nameof(DiagnosticsSettings.ShowOverlay) }));
    });
  }

  /// <summary>
  /// SCENARIO:
  /// ShowOverlay is set to its current value
  ///
  /// ACTION:
  /// Set true, subscribe, then set true again
  ///
  /// EXPECTED RESULT:
  /// PropertyChanged does not fire on the redundant assignment
  /// </summary>
  [Test]
  public void SetShowOverlay_SameValue_DoesNotFire()
  {
    var settings = new DiagnosticsSettings { ShowOverlay = true };
    var fired = false;
    settings.PropertyChanged += (_, _) => fired = true;

    settings.ShowOverlay = true;

    Assert.That(fired, Is.False);
  }
}
