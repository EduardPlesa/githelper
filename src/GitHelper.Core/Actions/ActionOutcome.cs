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
    IReadOnlyList<PreconditionResult> Blockers);
