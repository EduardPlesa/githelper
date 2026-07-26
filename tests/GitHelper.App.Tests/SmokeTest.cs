using Avalonia.Headless.XUnit;
using GitHelper.App.Views;

namespace GitHelper.App.Tests;

public class SmokeTest
{
    [Fact]
    public void PlainFactsRunWithoutAnyUiThread()
    {
        Assert.True(true);
    }

    [AvaloniaFact]
    public void MainWindowCanBeConstructedAndShown()
    {
        var window = new MainWindow();

        window.Show();

        Assert.True(window.IsVisible);
        Assert.Equal("GitHelper", window.Title);
    }
}
