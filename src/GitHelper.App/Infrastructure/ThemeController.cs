using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
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

        var variant = theme switch
        {
            AppTheme.Light => ThemeVariant.Light,
            AppTheme.Dark => ThemeVariant.Dark,

            // Avalonia's Default already means "follow the OS", so the app never has to
            // read the system setting itself.
            _ => ThemeVariant.Default,
        };

        // Application.RequestedThemeVariant has UI-thread affinity and throws outright when
        // set from anywhere else. Callers should not have to know that, so the hop lives
        // here — applied inline when already on the UI thread, so a caller that reads the
        // variant back immediately still sees it.
        if (Dispatcher.UIThread.CheckAccess())
            application.RequestedThemeVariant = variant;
        else
            Dispatcher.UIThread.Post(() => application.RequestedThemeVariant = variant);
    }
}
