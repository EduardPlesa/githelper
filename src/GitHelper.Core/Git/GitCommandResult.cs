namespace GitHelper.Core.Git;

/// <summary>The complete outcome of one git invocation.</summary>
/// <param name="ArgVector">
/// The user-facing arguments only. Internal flags such as -c core.quotepath=false are
/// deliberately excluded so that the command shown to the user is the command they could
/// type themselves.
/// </param>
public sealed record GitCommandResult(
    IReadOnlyList<string> ArgVector,
    string StdOut,
    string StdErr,
    int ExitCode,
    TimeSpan Duration)
{
    public bool Success => ExitCode == 0;

    /// <summary>The command as a user could type it. Used by the command log and explain panel.</summary>
    public string CommandLine => "git " + string.Join(' ', ArgVector);
}
