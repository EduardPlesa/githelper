using GitHelper.Core.Git;

namespace GitHelper.Core.Tests;

public class CommandLogTests
{
    private sealed class StubRunner(GitCommandResult result) : IGitRunner
    {
        public int Calls { get; private set; }

        public Task<GitCommandResult> RunAsync(
            string workingDirectory, IReadOnlyList<string> args, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    private static GitCommandResult Result(int exitCode = 0)
        => new(new[] { "status" }, "", "", exitCode, TimeSpan.FromMilliseconds(12));

    [Fact]
    public void Record_KeepsCommandsInTheOrderTheyRan()
    {
        var log = new CommandLog();

        log.Record(new GitCommandResult(new[] { "status" }, "", "", 0, TimeSpan.Zero));
        log.Record(new GitCommandResult(new[] { "add", "-A" }, "", "", 0, TimeSpan.Zero));

        Assert.Equal(new[] { "git status", "git add -A" }, log.Entries.Select(e => e.CommandLine));
    }

    [Fact]
    public void Record_CapturesFailureAsWellAsSuccess()
    {
        var log = new CommandLog();

        log.Record(Result(exitCode: 1));

        var entry = Assert.Single(log.Entries);
        Assert.False(entry.Success);
        Assert.Equal(1, entry.ExitCode);
    }

    [Fact]
    public void Record_RaisesAnEventSoTheUiCanAppendWithoutPolling()
    {
        var log = new CommandLog();
        CommandLogEntry? received = null;
        log.EntryRecorded += (_, entry) => received = entry;

        log.Record(Result());

        Assert.NotNull(received);
        Assert.Equal("git status", received!.CommandLine);
    }

    [Fact]
    public void ToClipboardText_ProducesCommandsAUserCouldPasteIntoATerminal()
    {
        var log = new CommandLog();
        log.Record(new GitCommandResult(new[] { "status" }, "", "", 0, TimeSpan.Zero));
        log.Record(new GitCommandResult(new[] { "add", "-A" }, "", "", 0, TimeSpan.Zero));

        var text = log.ToClipboardText();

        Assert.Equal("git status\ngit add -A", text.Replace("\r\n", "\n"));
    }

    [Fact]
    public void Clear_EmptiesTheLog()
    {
        var log = new CommandLog();
        log.Record(Result());

        log.Clear();

        Assert.Empty(log.Entries);
    }

    [Fact]
    public async Task LoggingGitRunner_RecordsEveryInvocationAndReturnsTheInnerResult()
    {
        var inner = new StubRunner(Result());
        var log = new CommandLog();
        var runner = new LoggingGitRunner(inner, log);

        var result = await runner.RunAsync(@"C:\repo", new[] { "status" });

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, inner.Calls);
        Assert.Single(log.Entries);
    }

    [Fact]
    public async Task LoggingGitRunner_CapturesReadOnlyQueriesToo()
    {
        // The user learns the CLI by watching it accumulate, and status is the command
        // they will most need to recognise.
        var log = new CommandLog();
        var runner = new LoggingGitRunner(new GitRunner(), log);
        using var repo = await TestRepo.CreateAsync();

        await runner.RunAsync(repo.Path, new[] { "status", "--porcelain=v2", "-z", "--branch" });

        Assert.Contains(log.Entries, e => e.CommandLine.StartsWith("git status"));
    }

    [Fact]
    public void Record_IsSafeToCallFromSeveralThreadsAtOnce()
    {
        var log = new CommandLog();

        Parallel.For(0, 500, _ => log.Record(Result()));

        Assert.Equal(500, log.Entries.Count);
    }
}
