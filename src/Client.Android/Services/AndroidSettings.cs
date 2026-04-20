using System.Diagnostics.CodeAnalysis;

namespace Client.Android.Services;

[ExcludeFromCodeCoverage]
public sealed class AndroidSettings
{
  private const string PrefsName = "simplevms.settings";
  private const string KeyStartOnBoot = "startOnBoot";

  private readonly global::Android.Content.Context _context;

  public AndroidSettings(global::Android.Content.Context context)
  {
    _context = context;
  }

  public bool StartOnBoot
  {
    get => Prefs.GetBoolean(KeyStartOnBoot, false);
    set
    {
      using var editor = Prefs.Edit()!;
      editor.PutBoolean(KeyStartOnBoot, value);
      editor.Apply();
    }
  }

  private global::Android.Content.ISharedPreferences Prefs =>
    _context.GetSharedPreferences(PrefsName, global::Android.Content.FileCreationMode.Private)!;
}
