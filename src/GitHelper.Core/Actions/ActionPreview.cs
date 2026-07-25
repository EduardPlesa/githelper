using GitHelper.Core.Content;

namespace GitHelper.Core.Actions;

/// <summary>
/// Everything the explain panel needs, produced without running anything.
/// </summary>
public sealed record ActionPreview(
    GitAction Action,
    IReadOnlyList<string> ArgVector,
    string CommandLine,
    ExplanationDocument Explanation,
    IReadOnlyDictionary<string, string> Slots,
    IReadOnlyList<PreconditionResult> Blockers,
    Danger Danger,
    string? UndoActionId)
{
    public bool CanRun => Blockers.Count == 0;
}
