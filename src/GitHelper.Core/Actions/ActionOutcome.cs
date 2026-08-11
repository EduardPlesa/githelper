using GitHelper.Core.Errors;
using GitHelper.Core.Git;
using GitHelper.Core.Model;

namespace GitHelper.Core.Actions;

/// <summary>The result of running an action, including what observably changed.</summary>
public sealed record ActionOutcome(
    bool Success,
    GitCommandResult Result,
    string? Narration,
    TranslatedError? Error,
    RepoState Before,
    RepoState After,
    IReadOnlyList<PreconditionResult> Blockers)
{
    /// <summary>
    /// True when the command left an operation in flight that was not in flight before.
    ///
    /// Derived from observed state rather than from the exit code, because a merge that
    /// stops on conflicts exits non-zero and is not a failure — git did exactly what it was
    /// asked. <see cref="Success"/> keeps meaning the exit code; callers that would
    /// otherwise show an error check this first.
    ///
    /// This only ever describes what a run just did. A merge already in flight when the
    /// repository was opened produces no outcome at all, and the band renders from
    /// <see cref="RepoState.Operation"/> rather than from here.
    /// </summary>
    public bool Paused => Before.Operation is null && After.Operation is not null;
}
