using GitHelper.Core.Model;

namespace GitHelper.Core.Actions;

/// <summary>
/// The outcome of one precondition. Failure messages are user-facing teaching copy:
/// they explain the underlying git concept rather than restating the obstacle.
/// </summary>
public sealed record PreconditionResult(
    bool Satisfied,
    string? Message = null,
    string? SuggestedActionId = null)
{
    public static readonly PreconditionResult Ok = new(true);

    public static PreconditionResult Fail(string message, string? suggestedActionId = null)
        => new(false, message, suggestedActionId);
}

public interface IPrecondition
{
    PreconditionResult Evaluate(RepoState state, ActionRequest request);
}

public sealed class RequiresPath : IPrecondition
{
    public PreconditionResult Evaluate(RepoState state, ActionRequest request)
        => string.IsNullOrWhiteSpace(request.Path)
            ? PreconditionResult.Fail("Pick a file first — this action works on one file at a time.")
            : PreconditionResult.Ok;
}

public sealed class RequiresMessage : IPrecondition
{
    public PreconditionResult Evaluate(RepoState state, ActionRequest request)
        => string.IsNullOrWhiteSpace(request.Message)
            ? PreconditionResult.Fail(
                "Every save needs a short description so you can recognise it later. "
                + "A few words about what you changed is enough.")
            : PreconditionResult.Ok;
}

public sealed class RequiresBranchName : IPrecondition
{
    public PreconditionResult Evaluate(RepoState state, ActionRequest request)
        => string.IsNullOrWhiteSpace(request.BranchName)
            ? PreconditionResult.Fail("Type a name for the branch.")
            : PreconditionResult.Ok;
}

public sealed class RequiresCommits : IPrecondition
{
    public PreconditionResult Evaluate(RepoState state, ActionRequest request)
        => state.HasCommits
            ? PreconditionResult.Ok
            : PreconditionResult.Fail(
                "This project has no saved versions yet. Make your first commit before doing this.",
                "commit");
}

public sealed class RequiresParentCommit : IPrecondition
{
    public PreconditionResult Evaluate(RepoState state, ActionRequest request)
        => state.CanUndoLastCommit
            ? PreconditionResult.Ok
            : PreconditionResult.Fail(
                "This is the first commit in the project, so there is no earlier version to step "
                + "back to. Undoing it this way is not possible.");
}

public sealed class RequiresStagedChanges : IPrecondition
{
    public PreconditionResult Evaluate(RepoState state, ActionRequest request)
        => state.HasStagedChanges
            ? PreconditionResult.Ok
            : PreconditionResult.Fail(
                "Nothing is staged yet. Editing a file is not the same as choosing it: you pick "
                + "which changes go into a commit by staging them first.",
                "stage-all");
}

public sealed class RequiresRemote : IPrecondition
{
    public PreconditionResult Evaluate(RepoState state, ActionRequest request)
        => state.HasRemote
            ? PreconditionResult.Ok
            : PreconditionResult.Fail(
                "This project has no online copy configured, so there is nowhere to send changes "
                + "to or fetch them from.");
}

public sealed class RequiresUpstream : IPrecondition
{
    public PreconditionResult Evaluate(RepoState state, ActionRequest request)
        => state.Upstream is not null
            ? PreconditionResult.Ok
            : PreconditionResult.Fail(
                "This branch has no upstream yet. The upstream is the branch on the server this one "
                + "is linked to, so git knows where to get changes from. Pushing once will set that link up.",
                "push");
}

public sealed class RequiresNotDetached : IPrecondition
{
    public PreconditionResult Evaluate(RepoState state, ActionRequest request)
        => state.IsDetached
            ? PreconditionResult.Fail(
                "You're not on a branch right now — git calls this a 'detached HEAD', and it "
                + "usually happens after checking out a specific commit or tag rather than a "
                + "branch. There's no branch name to send this to, so create one first.",
                "create-branch")
            : PreconditionResult.Ok;
}

public sealed class RequiresNoUncommittedChanges : IPrecondition
{
    public PreconditionResult Evaluate(RepoState state, ActionRequest request)
    {
        // Untracked files are carried across a switch untouched, so they are not an obstacle.
        var blocking = state.HasUncommittedChanges;

        return blocking
            ? PreconditionResult.Fail(
                "You have changes that are not saved yet. Switching now could mix them into "
                + "another branch, so commit them first.",
                "commit")
            : PreconditionResult.Ok;
    }
}

public sealed class RequiresNotCurrentBranch : IPrecondition
{
    public PreconditionResult Evaluate(RepoState state, ActionRequest request)
        => string.Equals(request.BranchName, state.Branch, StringComparison.Ordinal)
            ? PreconditionResult.Fail(
                "You are on this branch right now. Switch to a different branch before deleting it.")
            : PreconditionResult.Ok;
}

public sealed class RequiresBranchDoesNotExist : IPrecondition
{
    public PreconditionResult Evaluate(RepoState state, ActionRequest request)
        => state.Branches.Any(b => string.Equals(b.Name, request.BranchName, StringComparison.Ordinal))
            ? PreconditionResult.Fail(
                $"A branch called '{request.BranchName}' already exists. Pick a different name.")
            : PreconditionResult.Ok;
}
