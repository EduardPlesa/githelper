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
                // Delete, merge and rebase all use this, so the message names no one of them.
                "You are on this branch right now. Switch to a different branch first.")
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

public sealed class RequiresTagName : IPrecondition
{
    public PreconditionResult Evaluate(RepoState state, ActionRequest request)
        => string.IsNullOrWhiteSpace(request.TagName)
            ? PreconditionResult.Fail("Type a name for the tag.")
            : PreconditionResult.Ok;
}

public sealed class RequiresTagDoesNotExist : IPrecondition
{
    public PreconditionResult Evaluate(RepoState state, ActionRequest request)
        => state.Tags.Any(t => string.Equals(t.Name, request.TagName, StringComparison.Ordinal))
            ? PreconditionResult.Fail(
                $"A tag called '{request.TagName}' already exists. Pick a different name.")
            : PreconditionResult.Ok;
}

public sealed class RequiresUncommittedChanges : IPrecondition
{
    public PreconditionResult Evaluate(RepoState state, ActionRequest request)
        => state.HasUncommittedChanges
            ? PreconditionResult.Ok
            : PreconditionResult.Fail(
                "There is nothing to set aside — nothing has changed since your last commit.");
}

public sealed class RequiresStashRef : IPrecondition
{
    public PreconditionResult Evaluate(RepoState state, ActionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.StashRef))
            return PreconditionResult.Fail("Pick a stash first — this action works on one at a time.");

        return state.Stashes.Any(s => string.Equals(s.Ref, request.StashRef, StringComparison.Ordinal))
            ? PreconditionResult.Ok
            : PreconditionResult.Fail(
                "That stash is no longer there. It may already have been brought back, "
                + "deleted, or removed from outside this app.");
    }
}

public sealed class RequiresMergeInProgress : IPrecondition
{
    public PreconditionResult Evaluate(RepoState state, ActionRequest request)
        => state.Operation?.Kind == OperationKind.Merge
            ? PreconditionResult.Ok
            : PreconditionResult.Fail("There is no merge under way, so there is nothing to finish.");
}

public sealed class RequiresRebaseInProgress : IPrecondition
{
    public PreconditionResult Evaluate(RepoState state, ActionRequest request)
        => state.Operation?.Kind == OperationKind.Rebase
            ? PreconditionResult.Ok
            : PreconditionResult.Fail(
                "There is no update under way, so there is nothing to carry on with.");
}

/// <summary>
/// Satisfied by any operation, of any kind. Fixing a conflict is the same act whether it
/// came from a merge or a rebase, so mark-resolved uses this rather than either specific one.
/// </summary>
public sealed class RequiresOperationInProgress : IPrecondition
{
    public PreconditionResult Evaluate(RepoState state, ActionRequest request)
        => state.Operation is not null
            ? PreconditionResult.Ok
            : PreconditionResult.Fail(
                "Nothing is part-way through, so there is no conflict to mark as fixed.");
}

/// <summary>
/// Refuses anything that would rewrite commits already on a server. This app has no
/// force-push, so the rewritten branch could never be sent again — and the push refusal the
/// user would eventually hit says "the server has work you do not have yet", which would send
/// them to pull and make it worse. Refusing here, with the reason, is the honest version.
/// </summary>
public sealed class RequiresNoUpstream : IPrecondition
{
    public PreconditionResult Evaluate(RepoState state, ActionRequest request)
        => state.Upstream is null
            ? PreconditionResult.Ok
            : PreconditionResult.Fail(
                "This branch is already on the server. Updating it this way rewrites commits "
                + "that have been sent, which would need a force-push — something this app "
                + "deliberately does not do. Bring the other branch's work in instead.",
                "merge");
}

/// <summary>
/// Guards anything that must not run while git has an operation half-done. Most such
/// commands git refuses by itself; this exists for the ones that would quietly succeed.
///
/// The message is deliberately operation-neutral. This guard is satisfied by no operation of
/// any kind, so naming one would be wrong half the time — and the band it sends the user to
/// already names what is in flight, and labels its own buttons accordingly. Staying neutral
/// keeps this one sentence correct when cherry-pick and revert join the enum.
/// </summary>
public sealed class RequiresNoOperationInProgress : IPrecondition
{
    public PreconditionResult Evaluate(RepoState state, ActionRequest request)
        => state.Operation is null
            ? PreconditionResult.Ok
            : PreconditionResult.Fail(
                "You are part-way through something already — the banner at the top of the "
                + "window says what. Finish it or abandon it there before starting something "
                + "else.");
}

public sealed class RequiresNoUnmergedFiles : IPrecondition
{
    public PreconditionResult Evaluate(RepoState state, ActionRequest request)
    {
        var remaining = state.Unmerged.Count;

        return remaining == 0
            ? PreconditionResult.Ok
            : PreconditionResult.Fail(
                $"{remaining} file(s) still have changes git could not combine. Open each one, "
                + "fix the marked sections, and mark it fixed before finishing.");
    }
}

public sealed class RequiresNoRemote : IPrecondition
{
    public PreconditionResult Evaluate(RepoState state, ActionRequest request)
        => state.HasRemote
            ? PreconditionResult.Fail(
                "This project already has an online copy configured. Connecting a second one "
                + "would leave two addresses to keep straight, so disconnect the current one "
                + "first if you want to point it somewhere else.",
                "disconnect-remote")
            : PreconditionResult.Ok;
}

/// <summary>
/// The one value in this app that arrives from the clipboard and ends up in argv. Argv
/// arrays prevent shell injection but not argument injection: git reads a leading '-' as a
/// flag, so `--upload-pack=...` pasted here would be an instruction rather than a place.
/// The messages are written for someone who pasted the wrong thing, not for an attacker.
/// </summary>
public sealed class RequiresValidRemoteUrl : IPrecondition
{
    private const string HttpsPrefix = "https://";

    public PreconditionResult Evaluate(RepoState state, ActionRequest request)
    {
        var url = request.RemoteUrl?.Trim();

        if (string.IsNullOrEmpty(url))
            return PreconditionResult.Fail(
                "Paste the address of the empty repository you created on GitHub. GitHub shows "
                + "it on the page you land on straight after creating one.");

        if (url.StartsWith('-'))
            return PreconditionResult.Fail(
                "An address cannot start with a dash — git would read that as an instruction "
                + "rather than a place. Copy the address again from GitHub.");

        if (url.Any(char.IsWhiteSpace))
            return PreconditionResult.Fail(
                "That address has a space in it, so something else was probably copied along "
                + "with it. Copy just the address.");

        if (url.StartsWith("git@", StringComparison.Ordinal))
            return PreconditionResult.Ok;

        if (!url.StartsWith(HttpsPrefix, StringComparison.OrdinalIgnoreCase))
            return PreconditionResult.Fail(
                $"A project address starts with {HttpsPrefix} or git@. Copy it from the green "
                + "Code button on the project's page on GitHub.");

        if (AuthorityOf(url).Contains('@'))
            return PreconditionResult.Fail(
                "That address has a username or sign-in token built into it. Copy the plain "
                + "address from the green Code button instead — this app never needs your "
                + "token, and git asks for your sign-in itself the first time you send.");

        var segmentCount = ClonePathSegmentCount(url);

        if (segmentCount < 3)
            return PreconditionResult.Fail(
                "That address has no owner and project name in it. Copy the whole address "
                + "from the green Code button on the project's page.");

        if (segmentCount > 3)
            return PreconditionResult.Fail(
                "That is the address of a page inside the project rather than the project "
                + "itself. Go to the project's front page and copy the address from the green "
                + "Code button.");

        return PreconditionResult.Ok;
    }

    /// <summary>The authority is everything after the scheme and before the first '/'.</summary>
    private static string AuthorityOf(string url)
    {
        var afterScheme = url[HttpsPrefix.Length..];
        var slash = afterScheme.IndexOf('/');
        return slash < 0 ? afterScheme : afterScheme[..slash];
    }

    /// <summary>
    /// A clone address is host, owner, project — nothing more, nothing less. Fewer segments
    /// is a page that stops at the site itself; more is a page the user happened to be
    /// looking at: /tree/main, /settings, /pull/3.
    /// </summary>
    private static int ClonePathSegmentCount(string url)
    {
        var afterScheme = url[HttpsPrefix.Length..].TrimEnd('/');
        return afterScheme.Split('/', StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
