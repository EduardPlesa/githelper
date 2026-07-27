using GitHelper.Core.Content;

namespace GitHelper.Core.Setup;

/// <summary>
/// Everything the explain panel needs for a setup operation, produced without changing
/// anything. Exactly one of CommandLine and FileContents is non-null: `init` runs a command,
/// `create-gitignore` writes a file and has no command to show.
/// </summary>
public sealed record SetupPreview(
    string OperationId,
    string Title,
    ExplanationDocument Explanation,
    string? CommandLine,
    string? FileContents,
    IReadOnlyList<string> Blockers)
{
    public bool CanRun => Blockers.Count == 0;
}
