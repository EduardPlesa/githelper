using System.Diagnostics;

namespace GitHelper.App.Infrastructure;

/// <summary>
/// The real launcher. UseShellExecute hands the address to the operating system's default
/// handler rather than trying to locate a browser executable.
/// </summary>
public sealed class ShellBrowserLauncher : IBrowserLauncher
{
    public void Open(string url)
    {
        // Failing to open a browser must never take the app down with it: the user can
        // always reach github.com themselves, and the connect box is still usable.
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        // PlatformNotSupportedException belongs here with the other two: UseShellExecute
        // throws it on a runtime with no shell, and this project has no OS-specific target
        // framework ruling that out.
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception
                                      or InvalidOperationException
                                      or PlatformNotSupportedException)
        {
        }
    }
}
