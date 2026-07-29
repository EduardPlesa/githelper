using GitHelper.App.ViewModels;
using GitHelper.Core.Git;

namespace GitHelper.App.Tests;

public class CommandLogViewModelTests
{
    private static GitCommandResult Result(params string[] args)
        => new(args, "", "", 0, TimeSpan.FromMilliseconds(5));

    [Fact]
    public void SeedsFromCommandsAlreadyRecordedBeforeConstruction()
    {
        var log = new CommandLog();
        log.Record(Result("--version"));

        using var vm = new CommandLogViewModel(log, new StubDispatcher());

        Assert.Single(vm.Entries);
        Assert.Equal("git --version", vm.Entries[0].CommandLine);
    }

    [Fact]
    public void AppendsNewEntriesAsTheyAreRecorded()
    {
        var log = new CommandLog();
        using var vm = new CommandLogViewModel(log, new StubDispatcher());

        log.Record(Result("status"));
        log.Record(Result("add", "-A"));

        Assert.Equal(
            new[] { "git status", "git add -A" },
            vm.Entries.Select(e => e.CommandLine));
    }

    [Fact]
    public void MarshalsAppendsThroughTheDispatcher()
    {
        var log = new CommandLog();
        var dispatcher = new StubDispatcher { RunInline = false };
        using var vm = new CommandLogViewModel(log, dispatcher);

        log.Record(Result("status"));

        // Nothing appended, because the dispatcher was told not to run inline — proving the
        // append really goes through it rather than touching the collection directly.
        Assert.Empty(vm.Entries);
        Assert.Equal(1, dispatcher.PostCount);
    }

    [Fact]
    public async Task CommandsRecordedOnSeveralThreadsAtOnceAllLand()
    {
        // Two git-backed operations really do finish at the same moment in the app — an
        // action completing while the file watcher refreshes — and EntryRecorded fires on
        // whichever thread ran git. ObservableCollection is not thread-safe, so if the
        // dispatcher does not funnel those appends one at a time this corrupts its backing
        // list: either an IndexOutOfRangeException out of InsertItem, or a silently short
        // collection.
        var log = new CommandLog();
        using var vm = new CommandLogViewModel(log, new StubDispatcher());
        const int threads = 4;
        const int perThread = 250;
        using var ready = new Barrier(threads);

        var workers = Enumerable.Range(0, threads).Select(_ => Task.Run(() =>
        {
            ready.SignalAndWait();
            for (var i = 0; i < perThread; i++) log.Record(Result("status"));
        })).ToArray();

        await Task.WhenAll(workers);

        Assert.Equal(threads * perThread, vm.Entries.Count);
    }

    [Fact]
    public void ClipboardTextGivesPasteableCommands()
    {
        var log = new CommandLog();
        using var vm = new CommandLogViewModel(log, new StubDispatcher());
        log.Record(Result("commit", "-m", "add a file"));

        Assert.Equal("git commit -m \"add a file\"", vm.ClipboardText.Replace("\r\n", "\n"));
    }

    [Fact]
    public void Dispose_StopsListeningSoLaterCommandsAreIgnored()
    {
        var log = new CommandLog();
        var vm = new CommandLogViewModel(log, new StubDispatcher());

        vm.Dispose();
        log.Record(Result("status"));

        Assert.Empty(vm.Entries);
    }
}
