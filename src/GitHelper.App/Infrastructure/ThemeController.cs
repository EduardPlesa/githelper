using Avalonia;
using Avalonia.Styling;
using GitHelper.App.Settings;

namespace GitHelper.App.Infrastructure;

/// <summary>
/// The only place that changes the application's theme variant.
/// </summary>
public sealed class ThemeController
{
    public void Apply(AppTheme theme)
    {
        if (Application.Current is not { } application) return;

        application.RequestedThemeVariant = theme switch
        {
            AppTheme.Light => ThemeVariant.Light,
            AppTheme.Dark => ThemeVariant.Dark,

            // Avalonia's Default already means "follow the OS", so the app never has to
            // read the system setting itself.
            _ => ThemeVariant.Default,
        };
    }
}
