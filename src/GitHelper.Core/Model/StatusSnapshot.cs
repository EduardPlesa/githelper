namespace GitHelper.Core.Model;

/// <summary>The parsed result of one status invocation.</summary>
public sealed record StatusSnapshot(
    string? Branch,
    bool IsDetached,
    bool HasCommits,
    string? Upstream,
    int Ahead,
    int Behind,
    IReadOnlyList<FileChange> Changes);
