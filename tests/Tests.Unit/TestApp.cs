using Avalonia;
using Avalonia.Headless;
using Tests.Unit;

[assembly: AvaloniaTestApplication(typeof(TestApp))]

namespace Tests.Unit;

public sealed class TestApp : Application
{
  public static AppBuilder BuildAvaloniaApp() =>
    AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
