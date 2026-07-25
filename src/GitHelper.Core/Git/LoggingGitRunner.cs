namespace GitHelper.Core.Git;

/// <summary>
/// Records every invocation, then delegates. A decorator rather than logging inside
/// GitRunner: that class has one job, starting a process correctly, and it is the part of
/// the system least worth disturbing.
/// </summary>
public sealed class LoggingGitRunner(IGitRunner inner, CommandLog log) : IGitRunner
{
    public async Task<GitCommandResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> args,
        CancellationToken ct = default)
    {
        var result = await inner.RunAsync(workingDirectory, args, ct);
        log.Record(result);
        return result;
    }
}
