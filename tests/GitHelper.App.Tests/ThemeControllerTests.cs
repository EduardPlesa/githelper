using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using GitHelper.App.Infrastructure;
using GitHelper.App.Settings;

namespace GitHelper.App.Tests;

public class ThemeControllerTests
{
    [AvaloniaTheory]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.Dark)]
    public void Apply_SetsAnExplicitVariant(AppTheme theme)
    {
        new ThemeController().Apply(theme);

        var expected = theme == AppTheme.Light ? ThemeVariant.Light : ThemeVariant.Dark;
        Assert.Equal(expected, Application.Current!.RequestedThemeVariant);
    }

    [AvaloniaFact]
    public async Task Apply_FromABackgroundThreadDoesNotThrow()
    {
        // RequestedThemeVariant has UI-thread affinity. Any caller off the UI thread used to
        // crash with "Call from invalid thread", which made every plain [Fact] touching the
        // theme fail as soon as some other test had built the headless Application.
        var exception = await Record.ExceptionAsync(
            () => Task.Run(() => new ThemeController().Apply(AppTheme.Dark)));

        Assert.Null(exception);
    }

    [AvaloniaFact]
    public void Apply_SystemMeansFollowTheOperatingSystem()
    {
        new ThemeController().Apply(AppTheme.Dark);

        new ThemeController().Apply(AppTheme.System);

        // Default is Avalonia's "follow the OS" value.
        Assert.Equal(ThemeVariant.Default, Application.Current!.RequestedThemeVariant);
    }
}
