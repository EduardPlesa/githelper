namespace GitHelper.Core.Git;

/// <summary>One git command as it appeared to the user.</summary>
public sealed record CommandLogEntry(
    DateTimeOffset At,
    string CommandLine,
    int ExitCode,
    TimeSpan Duration,
    bool Success);
