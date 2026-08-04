using GitHelper.Core.Model;

namespace GitHelper.Core.Actions;

/// <summary>The v1 action set, expressed as data.</summary>
public static class ActionCatalog
{
    private static readonly IPrecondition[] None = Array.Empty<IPrecondition>();

    public static IReadOnlyList<GitAction> All { get; } = new[]
    {
        new GitAction(
            Id: "stage-file",
            Title: "Stage file",
            Danger: Danger.Safe,
            BuildArgs: (_, r) => new[] { "add", "--", r.Path! },
            Preconditions: new IPrecondition[] { new RequiresPath() },
            UndoActionId: "unstage-file"),

        new GitAction(
            Id: "unstage-file",
            Title: "Unstage file",
            Danger: Danger.Safe,
            // restore --staged needs a HEAD; before the first commit there is none.
            BuildArgs: (s, r) => s.HasCommits
                ? new[] { "restore", "--staged", "--", r.Path! }
                : new[] { "rm", "--cached", "--", r.Path! },
            Preconditions: new IPrecondition[] { new RequiresPath() },
            UndoActionId: "stage-file"),

        new GitAction(
            Id: "stage-all",
            Title: "Stage everything",
            Danger: Danger.Safe,
            BuildArgs: (_, _) => new[] { "add", "-A" },
            Preconditions: None,
            UndoActionId: "unstage-all"),

        new GitAction(
            Id: "unstage-all",
            Title: "Unstage everything",
            Danger: Danger.Safe,
            BuildArgs: (s, _) => s.HasCommits
                ? new[] { "restore", "--staged", "--", "." }
                : new[] { "rm", "--cached", "-r", "--", "." },
            Preconditions: None,
            UndoActionId: "stage-all"),

        new GitAction(
            Id: "commit",
            Title: "Commit",
            Danger: Danger.Caution,
            BuildArgs: (_, r) => new[] { "commit", "-m", r.Message! },
            Preconditions: new IPrecondition[] { new RequiresMessage(), new RequiresStagedChanges() },
            UndoActionId: "undo-last-commit"),

        new GitAction(
            Id: "create-branch",
            Title: "Create branch",
            Danger: Danger.Safe,
            BuildArgs: (_, r) => new[] { "switch", "-c", r.BranchName! },
            Preconditions: new IPrecondition[]
            {
                new RequiresBranchName(), new RequiresCommits(), new RequiresBranchDoesNotExist(),
            }),

        new GitAction(
            Id: "switch-branch",
            Title: "Switch branch",
            Danger: Danger.Caution,
            BuildArgs: (_, r) => new[] { "switch", r.BranchName! },
            Preconditions: new IPrecondition[]
            {
                new RequiresBranchName(), new RequiresNoUncommittedChanges(),
            }),

        new GitAction(
            Id: "fetch",
            Title: "Check for updates",
            Danger: Danger.Safe,
            BuildArgs: (_, _) => new[] { "fetch" },
            Preconditions: new IPrecondition[] { new RequiresRemote() }),

        new GitAction(
            Id: "pull",
            Title: "Get changes from the server",
            Danger: Danger.Caution,
            // --ff-only: refuse rather than silently create a merge commit the user
            // did not ask for and could not explain.
            BuildArgs: (_, _) => new[] { "pull", "--ff-only" },
            Preconditions: new IPrecondition[] { new RequiresRemote(), new RequiresUpstream() }),

        new GitAction(
            Id: "push",
            Title: "Send changes to the server",
            Danger: Danger.Caution,
            BuildArgs: (s, _) => s.Upstream is null
                ? new[] { "push", "--set-upstream", "origin", s.Branch! }
                : new[] { "push" },
            Preconditions: new IPrecondition[]
            {
                new RequiresRemote(), new RequiresCommits(), new RequiresNotDetached(),
            }),

        new GitAction(
            Id: "discard-file",
            Title: "Discard changes to file",
            Danger: Danger.Destructive,
            BuildArgs: (_, r) => new[] { "restore", "--", r.Path! },
            Preconditions: new IPrecondition[] { new RequiresPath() }),

        new GitAction(
            Id: "undo-last-commit",
            Title: "Undo last commit",
            Danger: Danger.Caution,
            // --soft: the commit is removed but the work stays, staged and safe.
            BuildArgs: (_, _) => new[] { "reset", "--soft", "HEAD~1" },
            Preconditions: new IPrecondition[] { new RequiresCommits(), new RequiresParentCommit() }),

        new GitAction(
            Id: "delete-branch",
            Title: "Delete branch",
            Danger: Danger.Caution,
            // -d, never -D: git refuses to delete a branch holding unmerged work,
            // and that refusal is explained rather than overridden.
            BuildArgs: (_, r) => new[] { "branch", "-d", r.BranchName! },
            Preconditions: new IPrecondition[]
            {
                new RequiresBranchName(), new RequiresNotCurrentBranch(),
            }),

        new GitAction(
            Id: "connect-remote",
            Title: "Connect to GitHub",
            Danger: Danger.Caution,
            // Trimmed here as well as in the precondition: the two must agree on the exact
            // string, and the user's paste routinely carries trailing whitespace.
            BuildArgs: (_, r) => new[] { "remote", "add", "origin", r.RemoteUrl!.Trim() },
            Preconditions: new IPrecondition[]
            {
                new RequiresNoRemote(), new RequiresValidRemoteUrl(),
            },
            UndoActionId: "disconnect-remote"),

        new GitAction(
            Id: "disconnect-remote",
            Title: "Disconnect from GitHub",
            Danger: Danger.Caution,
            BuildArgs: (_, _) => new[] { "remote", "remove", "origin" },
            Preconditions: new IPrecondition[] { new RequiresRemote() }),

        new GitAction(
            Id: "create-tag",
            Title: "Tag this point",
            Danger: Danger.Safe,
            BuildArgs: (_, r) => new[] { "tag", r.TagName! },
            Preconditions: new IPrecondition[]
            {
                new RequiresTagName(), new RequiresCommits(), new RequiresTagDoesNotExist(),
            },
            UndoActionId: "delete-tag"),

        new GitAction(
            Id: "delete-tag",
            Title: "Delete tag",
            Danger: Danger.Caution,
            // Unlike branch -d, git has no refusal safety net here — tag -d always succeeds.
            BuildArgs: (_, r) => new[] { "tag", "-d", r.TagName! },
            Preconditions: new IPrecondition[] { new RequiresTagName() }),

        new GitAction(
            Id: "stash",
            Title: "Set changes aside",
            Danger: Danger.Safe,
            BuildArgs: (_, r) => string.IsNullOrWhiteSpace(r.Message)
                ? new[] { "stash", "push" }
                : new[] { "stash", "push", "-m", r.Message! },
            Preconditions: new IPrecondition[] { new RequiresUncommittedChanges() },
            UndoActionId: "stash-pop"),

        new GitAction(
            Id: "stash-pop",
            Title: "Bring back stashed changes",
            Danger: Danger.Caution,
            BuildArgs: (_, r) => new[] { "stash", "pop", r.StashRef! },
            // Only offered against a clean tree, so this can never land on other unsaved
            // edits and conflict with them -- the app has no operation-state model yet.
            Preconditions: new IPrecondition[] { new RequiresStashRef(), new RequiresNoUncommittedChanges() }),

        new GitAction(
            Id: "stash-apply",
            Title: "Copy back stashed changes",
            Danger: Danger.Caution,
            BuildArgs: (_, r) => new[] { "stash", "apply", r.StashRef! },
            Preconditions: new IPrecondition[] { new RequiresStashRef(), new RequiresNoUncommittedChanges() }),

        new GitAction(
            Id: "stash-drop",
            Title: "Delete stash",
            Danger: Danger.Destructive,
            BuildArgs: (_, r) => new[] { "stash", "drop", r.StashRef! },
            Preconditions: new IPrecondition[] { new RequiresStashRef() }),
    };

    private static readonly Dictionary<string, GitAction> ById =
        All.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);

    public static GitAction? Find(string actionId)
        => ById.TryGetValue(actionId, out var action) ? action : null;
}
