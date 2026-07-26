using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(GitHelper.App.Tests.TestAppBuilder))]

namespace GitHelper.App.Tests;

/// <summary>
/// Builds the real application against Avalonia's headless platform, so tests marked
/// [AvaloniaFact] get a working UI thread with no window ever appearing on screen.
/// Tests that need no UI thread at all should use a plain [Fact].
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<global::GitHelper.App.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
