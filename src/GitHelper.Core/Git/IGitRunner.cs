namespace GitHelper.Core.Git;

/// <summary>
/// The single choke point for git access. Nothing else in the application may
/// start a process.
/// </summary>
public interface IGitRunner
{
    Task<GitCommandResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> args,
        CancellationToken ct = default);
}
