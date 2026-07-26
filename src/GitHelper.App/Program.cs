using Avalonia;

namespace GitHelper.App;

internal static class Program
{
    // STAThread is required on Windows for the classic desktop lifetime.
    [STAThread]
    public static void Main(string[] args)
        => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    /// <summary>Also used by the headless test harness, which swaps the platform.</summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
