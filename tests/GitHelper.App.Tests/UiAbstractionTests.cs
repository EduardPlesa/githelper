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

    [Fact]
    public async Task Post_NeverRunsTwoActionsAtOnce()
    {
        // Avalonia's dispatcher runs everything on the single UI thread, so a posted action
        // can never overlap another. Tests lean on that: a journey test has the file watcher
        // refreshing on a thread-pool thread while an action runs on the test thread, and
        // both post appends into the same ObservableCollection. A stub that ran callbacks on
        // whichever thread called Post let those appends collide.
        var dispatcher = new StubDispatcher();
        var inside = 0;
        var overlapped = false;
        const int threads = 4;
        using var ready = new Barrier(threads);

        var workers = Enumerable.Range(0, threads).Select(_ => Task.Run(() =>
        {
            ready.SignalAndWait();
            for (var i = 0; i < 250; i++)
                dispatcher.Post(() =>
                {
                    if (Interlocked.Increment(ref inside) > 1) overlapped = true;
                    Thread.Sleep(0); // widen the window so an unserialized stub fails reliably
                    Interlocked.Decrement(ref inside);
                });
        })).ToArray();

        await Task.WhenAll(workers);

        Assert.False(overlapped);
        Assert.Equal(threads * 250, dispatcher.PostCount);
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

public class StubBrowserLauncherTests
{
    [Fact]
    public void RecordsTheUrlItWasAskedToOpen()
    {
        var browser = new StubBrowserLauncher();

        browser.Open("https://github.com/new");

        Assert.Equal("https://github.com/new", browser.LastUrl);
        Assert.Equal(1, browser.CallCount);
    }
}
