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
    public void Apply_SystemMeansFollowTheOperatingSystem()
    {
        new ThemeController().Apply(AppTheme.Dark);

        new ThemeController().Apply(AppTheme.System);

        // Default is Avalonia's "follow the OS" value.
        Assert.Equal(ThemeVariant.Default, Application.Current!.RequestedThemeVariant);
    }
}
