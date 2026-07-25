using GitHelper.Core.Model;

namespace GitHelper.Core.Actions;

/// <summary>
/// One action, expressed as data. Adding an action is a descriptor plus a content file,
/// with no new UI code and no new branches in the preview/run flow.
/// </summary>
public sealed record GitAction(
    string Id,
    string Title,
    Danger Danger,
    Func<RepoState, ActionRequest, IReadOnlyList<string>> BuildArgs,
    IReadOnlyList<IPrecondition> Preconditions,
    string? UndoActionId = null)
{
    /// <summary>
    /// The content file id, equal to the action id by convention. Keeping these the same
    /// string rather than two fields removes a whole class of mismatch.
    /// </summary>
    public string ExplanationId => Id;
}
