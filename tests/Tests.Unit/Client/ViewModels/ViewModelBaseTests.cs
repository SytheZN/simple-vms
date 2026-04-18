using System.ComponentModel;
using Client.Core.Api;
using Client.Core.ViewModels;
using Shared.Models;

namespace Tests.Unit.Client.ViewModels;

[TestFixture]
public class ViewModelBaseTests
{
  private sealed class TestVm : ViewModelBase
  {
    private string? _name;
    public string? Name
    {
      get => _name;
      set => SetProperty(ref _name, value);
    }
    public new void SetError(Error error, HttpDiagnostics? diag = null) => base.SetError(error, diag);
    public new void ClearError() => base.ClearError();
    public new void OnPropertyChanged(string? name = null) => base.OnPropertyChanged(name);
  }

  /// <summary>
  /// SCENARIO:
  /// SetProperty on a value identical to the current field
  ///
  /// ACTION:
  /// Subscribe to PropertyChanged, assign the same value
  ///
  /// EXPECTED RESULT:
  /// PropertyChanged does not fire
  /// </summary>
  [Test]
  public void SetProperty_UnchangedValue_DoesNotNotify()
  {
    var vm = new TestVm { Name = "Foo" };
    var fired = false;
    vm.PropertyChanged += (_, _) => fired = true;

    vm.Name = "Foo";

    Assert.That(fired, Is.False);
  }

  /// <summary>
  /// SCENARIO:
  /// SetProperty on a different value
  ///
  /// ACTION:
  /// Subscribe to PropertyChanged, assign new value
  ///
  /// EXPECTED RESULT:
  /// PropertyChanged fires once with the property name
  /// </summary>
  [Test]
  public void SetProperty_ChangedValue_NotifiesByName()
  {
    var vm = new TestVm();
    var names = new List<string?>();
    vm.PropertyChanged += (_, e) => names.Add(e.PropertyName);

    vm.Name = "Bar";

    Assert.That(names, Is.EqualTo(new[] { nameof(TestVm.Name) }));
  }

  /// <summary>
  /// SCENARIO:
  /// OnPropertyChanged is invoked manually for a computed/derived property
  ///
  /// ACTION:
  /// Call OnPropertyChanged("Computed")
  ///
  /// EXPECTED RESULT:
  /// PropertyChanged fires with the supplied name
  /// </summary>
  [Test]
  public void OnPropertyChanged_FiresForArbitraryName()
  {
    var vm = new TestVm();
    string? observed = null;
    vm.PropertyChanged += (_, e) => observed = e.PropertyName;

    vm.OnPropertyChanged("Computed");

    Assert.That(observed, Is.EqualTo("Computed"));
  }

  /// <summary>
  /// SCENARIO:
  /// SetError populates ErrorMessage / ErrorTag / ErrorJson from a plain Error
  ///
  /// ACTION:
  /// Build an Error, call SetError, read the three properties
  ///
  /// EXPECTED RESULT:
  /// Message and tag are surfaced; JSON contains result/debugTag/message keys
  /// and does NOT include url/httpStatus/rawBody (no diag supplied)
  /// </summary>
  [Test]
  public void SetError_NoDiag_PopulatesCoreFields()
  {
    var vm = new TestVm();
    var error = new Error(Result.NotFound, new DebugTag(0xABCD0001), "Not here");

    vm.SetError(error);

    Assert.Multiple(() =>
    {
      Assert.That(vm.ErrorMessage, Is.EqualTo("Not here"));
      Assert.That(vm.ErrorTag, Is.EqualTo(error.Tag));
      Assert.That(vm.ErrorJson, Does.Contain("\"result\":\"NotFound\""));
      Assert.That(vm.ErrorJson, Does.Contain("\"message\":\"Not here\""));
      Assert.That(vm.ErrorJson, Does.Not.Contain("\"url\""));
      Assert.That(vm.ErrorJson, Does.Not.Contain("\"httpStatus\""));
      Assert.That(vm.ErrorJson, Does.Not.Contain("\"rawBody\""));
    });
  }

  /// <summary>
  /// SCENARIO:
  /// SetError is called with HttpDiagnostics carrying url, status, raw body
  ///
  /// ACTION:
  /// Pass a fully populated HttpDiagnostics
  ///
  /// EXPECTED RESULT:
  /// All three diag fields appear in ErrorJson
  /// </summary>
  [Test]
  public void SetError_WithDiag_IncludesUrlStatusBody()
  {
    var vm = new TestVm();
    var error = new Error(Result.InternalError, new DebugTag(0x00010002), "Boom");
    var diag = new HttpDiagnostics("https://api/example", 502, "<html>Bad Gateway</html>");

    vm.SetError(error, diag);

    Assert.Multiple(() =>
    {
      Assert.That(vm.ErrorJson, Does.Contain("\"url\":\"https://api/example\""));
      Assert.That(vm.ErrorJson, Does.Contain("\"httpStatus\":502"));
      Assert.That(vm.ErrorJson, Does.Contain("\"rawBody\":\"<html>Bad Gateway</html>\""));
    });
  }

  /// <summary>
  /// SCENARIO:
  /// HttpDiagnostics has only the url; status and body are null
  ///
  /// ACTION:
  /// Pass diag with StatusCode = null and RawBody = null
  ///
  /// EXPECTED RESULT:
  /// JSON has the url key but skips httpStatus and rawBody
  /// </summary>
  [Test]
  public void SetError_DiagPartialFields_OmitsNullProperties()
  {
    var vm = new TestVm();
    var diag = new HttpDiagnostics("u", null, null);

    vm.SetError(new Error(Result.Forbidden, new DebugTag(1), "no"), diag);

    Assert.Multiple(() =>
    {
      Assert.That(vm.ErrorJson, Does.Contain("\"url\":\"u\""));
      Assert.That(vm.ErrorJson, Does.Not.Contain("httpStatus"));
      Assert.That(vm.ErrorJson, Does.Not.Contain("rawBody"));
    });
  }

  /// <summary>
  /// SCENARIO:
  /// The error message contains characters that need JSON escaping (quote,
  /// backslash, newline, carriage return)
  ///
  /// ACTION:
  /// Set an error with a message that includes all four characters
  ///
  /// EXPECTED RESULT:
  /// ErrorJson escapes quote and backslash, replaces \n with \\n, drops \r
  /// </summary>
  [Test]
  public void SetError_MessageWithSpecialChars_EscapesProperly()
  {
    var vm = new TestVm();
    var msg = "a\"b\\c\nd\re";

    vm.SetError(new Error(Result.InternalError, new DebugTag(1), msg));

    Assert.That(vm.ErrorJson, Does.Contain(@"a\""b\\c\nde"));
  }

  /// <summary>
  /// SCENARIO:
  /// ClearError resets all three error properties to null
  ///
  /// ACTION:
  /// Set an error, then ClearError
  ///
  /// EXPECTED RESULT:
  /// All three error properties read null
  /// </summary>
  [Test]
  public void ClearError_ResetsAllFields()
  {
    var vm = new TestVm();
    vm.SetError(new Error(Result.InternalError, new DebugTag(1), "x"));

    vm.ClearError();

    Assert.Multiple(() =>
    {
      Assert.That(vm.ErrorMessage, Is.Null);
      Assert.That(vm.ErrorTag, Is.Null);
      Assert.That(vm.ErrorJson, Is.Null);
    });
  }
}
