using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using GitHelper.App.Infrastructure;
using GitHelper.App.Settings;

namespace GitHelper.App.Tests;

public class StubDispatcherTests
{
    [Fact]
    public void RunInline_InvokesTheCallbackImmediately()
    {
        var dispatcher = new StubDispatcher();
        var ran = false;

        dispatcher.Post(() => ran = true);

        Assert.True(ran);
        Assert.Equal(1, dispatcher.PostCount);
    }

    [Fact]
    public void RunInlineDisabled_RecordsWithoutInvoking()
    {
        var dispatcher = new StubDispatcher { RunInline = false };
        var ran = false;

        dispatcher.Post(() => ran = true);

        Assert.False(ran);
        Assert.Equal(1, dispatcher.PostCount);
    }
}

public class StubFolderPickerTests
{
    [Fact]
    public async Task ReturnsTheConfiguredResultAndRecordsTheCall()
    {
        var picker = new StubFolderPicker { NextResult = @"C:\repos\demo" };

        var chosen = await picker.PickFolderAsync("Open a repository");

        Assert.Equal(@"C:\repos\demo", chosen);
        Assert.Equal(1, picker.CallCount);
        Assert.Equal("Open a repository", picker.LastTitle);
    }

    [Fact]
    public async Task ReturnsNullWhenTheUserCancels()
    {
        var picker = new StubFolderPicker { NextResult = null };

        Assert.Null(await picker.PickFolderAsync("Open a repository"));
    }
}

public class InMemorySettingsStoreTests
{
    [Fact]
    public void SaveThenLoad_ReturnsWhatWasSaved()
    {
        var store = new InMemorySettingsStore();

        store.Save(AppSettings.Default.WithTheme(AppTheme.Dark));

        Assert.Equal(AppTheme.Dark, store.Load().Theme);
        Assert.Equal(1, store.SaveCount);
    }
}

public class AvaloniaUiDispatcherTests
{
    [AvaloniaFact]
    public void Post_RunsSynchronouslyWhenAlreadyOnTheUiThread()
    {
        var dispatcher = new AvaloniaUiDispatcher();
        var ran = false;

        // No pumping between Post and the assertion: if this were queued rather than
        // run inline, `ran` would still be false here.
        dispatcher.Post(() => ran = true);

        Assert.True(ran);
        Assert.True(dispatcher.IsOnUiThread);
    }

    [AvaloniaFact]
    public void Post_FromABackgroundThreadReachesTheUiThread()
    {
        var dispatcher = new AvaloniaUiDispatcher();
        var reachedUiThread = false;

        Task.Run(() => dispatcher.Post(() => reachedUiThread = Dispatcher.UIThread.CheckAccess())).Wait();
        Dispatcher.UIThread.RunJobs();

        Assert.True(reachedUiThread);
    }
}
