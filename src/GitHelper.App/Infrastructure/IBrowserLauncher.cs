namespace GitHelper.App.Infrastructure;

/// <summary>
/// Opens a URL in whatever browser the user has. A seam, mirroring IFolderPicker, so that
/// viewmodels stay free of platform calls and a test can assert the address without a
/// browser window appearing.
/// </summary>
public interface IBrowserLauncher
{
    void Open(string url);
}
