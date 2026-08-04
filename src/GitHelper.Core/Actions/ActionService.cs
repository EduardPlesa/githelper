using GitHelper.Core.Content;
using GitHelper.Core.Errors;
using GitHelper.Core.Git;
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
        var after = await reader.ReadAsync(repoPath, ct);

        return new ActionOutcome(
            Success: result.Success,
            Result: result,
            Narration: result.Success ? Narrator.Describe(before, after) : null,
            Error: ErrorTranslator.Translate(result),
            Before: before,
            After: after,
            Blockers: Array.Empty<PreconditionResult>());
    }

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
