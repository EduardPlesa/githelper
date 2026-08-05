using GitHelper.Core.Content;
using GitHelper.Core.Errors;
using GitHelper.Core.Git;
using GitHelper.Core.Model;
using GitHelper.Core.Repo;

namespace GitHelper.Core.Actions;

/// <summary>The preview-then-run flow that every action goes through.</summary>
public sealed class ActionService(
    IGitRunner runner,
    RepoStateReader reader,
    ContentLibrary content)
{
    /// <summary>
    /// Builds everything the explain panel needs. Runs no git command that changes anything —
    /// only the read-only queries needed to describe what would happen.
    /// </summary>
    public async Task<ActionPreview> PreviewAsync(
        string repoPath,
        ActionRequest request,
        CancellationToken ct = default)
    {
        var action = Resolve(request.ActionId);
        var state = await reader.ReadAsync(repoPath, ct);

        var blockers = Evaluate(action, state, request);

        // Slots are bound only for an action that could actually run, for the same reason
        // argv is: a rejected value is the user's mistake echoed back at them, and one of
        // the things this rejects is a sign-in token.
        var slots = SlotBinder.Bind(
            state, request.Path, request.BranchName, request.TagName,
            blockers.Count == 0 ? request.RemoteUrl : null);

        // argv is only built when it can be built; a missing path would throw otherwise.
        var args = blockers.Count == 0
            ? action.BuildArgs(state, request)
            : Array.Empty<string>();

        var commandLine = args.Count == 0
            ? string.Empty
            : new GitCommandResult(args, string.Empty, string.Empty, 0, TimeSpan.Zero).CommandLine;

        return new ActionPreview(
            Action: action,
            ArgVector: args,
            CommandLine: commandLine,
            Explanation: content.Actions[action.ExplanationId],
            Slots: slots,
            Blockers: blockers,
            Danger: action.Danger,
            UndoActionId: action.UndoActionId);
    }

    /// <summary>
    /// Runs the action. Preconditions are re-evaluated here rather than trusted from the
    /// preview: the caller is not trusted, and state may have changed since.
    /// </summary>
    public async Task<ActionOutcome> RunAsync(
        string repoPath,
        ActionRequest request,
        CancellationToken ct = default)
    {
        var action = Resolve(request.ActionId);
        var before = await reader.ReadAsync(repoPath, ct);

        var blockers = Evaluate(action, before, request);
        if (blockers.Count > 0)
        {
            return new ActionOutcome(
                Success: false,
                Result: new GitCommandResult(Array.Empty<string>(), "", "", 0, TimeSpan.Zero),
                Narration: null,
                Error: null,
                Before: before,
                After: before,
                Blockers: blockers);
        }

        var args = action.BuildArgs(before, request);
        var result = await runner.RunAsync(repoPath, args, ct);

        // A stash pop/apply is a three-way merge against the commit the stash was taken
        // from, so it can still conflict even against a clean tree if HEAD has moved since —
        // the precondition only rules out unsaved edits, not that. The precondition did prove
        // the tree was clean immediately before this ran, so a hard reset should restore
        // exactly that state; the stash entry survives, because git only drops it on a clean
        // merge. Verified below rather than assumed: the copy this leads to tells the user
        // nothing was lost, so that claim has to be checked, not hoped for.
        var attemptedRollback = false;
        var rollbackFailed = false;
        if (!result.Success && IsStashMergeConflict(action.Id, result))
        {
            attemptedRollback = true;
            var rollback = await runner.RunAsync(repoPath, new[] { "reset", "--hard" }, ct);
            rollbackFailed = !rollback.Success;
        }

        var after = await reader.ReadAsync(repoPath, ct);

        // Gated on attemptedRollback, not just StillMidConflict: an unrelated conflict
        // sitting in the repo from something outside this action (a terminal merge, an
        // earlier rollback that itself failed to clear) must not turn an unrelated action's
        // success -- or failure -- into this stash-specific message.
        if (attemptedRollback && (rollbackFailed || StillMidConflict(after)))
        {
            var conflictedPaths = after.Changes
                .Where(c => c.IndexChange == ChangeKind.Unmerged || c.WorkTreeChange == ChangeKind.Unmerged)
                .Select(c => c.Path)
                .ToList();
            var fileList = conflictedPaths.Count == 0
                ? "the files marked *conflicted* in the list of changes"
                : string.Join(", ", conflictedPaths);

            return new ActionOutcome(
                Success: false,
                Result: result,
                Narration: null,
                Error: new TranslatedError(
                    Summary: "That clash could not be fully cleared automatically",
                    Explanation:
                        "Bringing the stash back conflicted with commits made since it was set "
                        + "aside, and putting your files back to how they were beforehand did "
                        + "not fully succeed. Some files may still show conflict markers.",
                    NextSteps: new[]
                    {
                        $"Open {fileList} and look for lines starting with <<<<<<<. This app "
                        + "does not yet walk you through resolving a clash like this.",
                        "Once resolved, the stash may still be listed — check before assuming "
                        + "it needs bringing back again.",
                    },
                    RawOutput: (result.StdErr + "\n" + result.StdOut).Trim(),
                    IsUnderstood: true),
                Before: before,
                After: after,
                Blockers: Array.Empty<PreconditionResult>());
        }

        return new ActionOutcome(
            Success: result.Success,
            Result: result,
            Narration: result.Success ? Narrator.Describe(before, after) : null,
            Error: ErrorTranslator.Translate(result),
            Before: before,
            After: after,
            Blockers: Array.Empty<PreconditionResult>());
    }

    /// <summary>
    /// True if the repository still shows an unmerged file after an attempted rollback —
    /// the one signal that "nothing was lost" would be a lie rather than a fact.
    /// </summary>
    private static bool StillMidConflict(Model.RepoState after)
        => after.Changes.Any(c => c.IndexChange == ChangeKind.Unmerged || c.WorkTreeChange == ChangeKind.Unmerged);

    private static bool IsStashMergeConflict(string actionId, GitCommandResult result)
        => (actionId == "stash-pop" || actionId == "stash-apply")
           && (result.StdErr + result.StdOut).Contains("CONFLICT", StringComparison.OrdinalIgnoreCase);

    private static GitAction Resolve(string actionId)
        => ActionCatalog.Find(actionId)
           ?? throw new ArgumentException($"Unknown action id '{actionId}'.", nameof(actionId));

    private static IReadOnlyList<PreconditionResult> Evaluate(
        GitAction action, Model.RepoState state, ActionRequest request)
        => action.Preconditions
            .Select(p => p.Evaluate(state, request))
            .Where(r => !r.Satisfied)
            .ToList();
}
