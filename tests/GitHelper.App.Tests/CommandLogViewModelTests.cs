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
