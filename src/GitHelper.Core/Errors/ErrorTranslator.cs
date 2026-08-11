using GitHelper.Core.Git;

namespace GitHelper.Core.Errors;

/// <summary>Turns git's stderr into plain English, or admits when it cannot.</summary>
public static class ErrorTranslator
{
    private sealed record Rule(
        string Pattern,
        string Summary,
        string Explanation,
        string[] NextSteps);

    /// <summary>Ordered, first match wins. Specific patterns must precede general ones.</summary>
    private static readonly Rule[] Rules =
    {
        // Ahead of "non-fast-forward" deliberately: git reports "(fetch first)" when the
        // server has a commit this copy has never seen, and "(non-fast-forward)" only once
        // it has been fetched and the two have diverged. Both causes below produce the
        // former, so the copy names both rather than guessing.
        new("(fetch first)",
            "The server has work your copy has not seen",
            "Your send was refused because the copy on the server has a commit yours knows "
            + "nothing about. Either someone else pushed and you have not fetched yet, or — if "
            + "this was your first send — the repository was created with a README, a .gitignore, "
            + "or a licence, which GitHub commits for you.",
            new[]
            {
                "Get the changes from the server first, then send yours again.",
                "If this was your first send, the repository was created with files in it: make a "
                + "new one with every 'add a file' option unticked, disconnect, and connect to that.",
            }),

        new("non-fast-forward",
            "The server has work you do not have yet",
            "Your send was rejected because someone else added commits to this branch after you "
            + "last got them. Git refuses rather than overwrite their work.",
            new[]
            {
                "Get the changes from the server first.",
                "Then send yours again.",
            }),

        new("no upstream branch",
            "This branch has no upstream branch on the server yet",
            "Git does not know which branch on the server this one belongs with, so it does not "
            + "know where to send your work. Sending once will set that link up.",
            new[] { "Send your changes; this app will set up the link at the same time." }),

        new("not a git repository",
            "This folder is not a git project",
            "Git keeps its history in a hidden .git folder, and there is not one here or in any "
            + "folder above it.",
            new[]
            {
                "Open a different folder.",
                "Or turn this folder into a git project first.",
            }),

        new("would be overwritten",
            "You have unsaved changes in the way",
            "Doing this would overwrite edits you have not committed, so git stopped instead of "
            + "losing them.",
            new[]
            {
                "Commit your changes, then try again.",
                "Or discard them if you do not want them.",
            }),

        new("authentication failed",
            "The server would not let you sign in",
            "Your saved sign-in details were refused. This app never handles your password — "
            + "Windows stores it for git in Credential Manager.",
            new[]
            {
                "Check that you still have access to this project.",
                "Update the saved credentials in Windows Credential Manager.",
            }),

        new("not fully merged",
            "That branch has work that exists nowhere else",
            "The branch holds commits that are not part of any other branch, so deleting it would "
            + "be the only way to lose them. Git refused on purpose.",
            new[]
            {
                "Look through the branch to see whether you still want that work.",
                "Merge it somewhere first if you do.",
            }),

        new("repository not found",
            "There is no project at that address",
            "Git reached the server, but found nothing at the address this project is "
            + "connected to. Either the address has a typo in it, or the repository is "
            + "private and this computer has not been given access.",
            new[]
            {
                "Check the address against the one GitHub shows on the project's page.",
                "Disconnect from GitHub and connect again with the corrected address.",
            }),

        new("does not appear to be a git repository",
            "The server address does not work",
            "Git could not find a project at the address configured for this remote.",
            new[]
            {
                "Check the address against the project's page on GitHub.",
                "Disconnect from GitHub and connect again with the corrected address.",
            }),

        new("not possible to fast-forward",
            "Both you and the server have new work",
            "A fast-forward only works when you have made nothing new. Since both sides have "
            + "commits, the two histories have to be combined, which this app does not do yet.",
            new[]
            {
                "Save or set aside your local commits.",
                "Combining histories is not supported in this version.",
            }),

        new("please tell me who you are",
            "Git does not know who you are yet",
            "Every commit records an author's name and email, and git has not been told yours. "
            + "This is a one-time setup, not an account — no password is involved.",
            new[]
            {
                "Set your name and email for git, then commit again: "
                + "git config --global user.name \"Your Name\" and "
                + "git config --global user.email \"you@example.com\".",
            }),

        new("nothing to commit",
            "There is nothing staged to save",
            "Editing a file is not the same as choosing it. You pick which changes go into a "
            + "commit by staging them first.",
            new[] { "Stage the files you want to save, then commit." }),

        new("pathspec",
            "Git could not find that file or branch",
            "The name given does not match a file or a branch that git knows about.",
            new[] { "Check the spelling, and that the file has not been moved or deleted." }),

        new("no stash entries found",
            "That stash is no longer there",
            "There is nothing stashed right now. It may already have been brought back, "
            + "deleted, or removed from outside this app.",
            new[] { "Refresh and check the list again." }),

        // Reachable only via stash-pop/stash-apply in this app (nothing else here can
        // produce a three-way-merge conflict). ActionService verifies the rollback actually
        // cleared the tree before this copy is ever shown — if it did not, it returns a
        // different error instead, so "put back" here stays a fact rather than a hope.
        new("CONFLICT",
            "That stash clashes with what's on this branch now",
            "Bringing it back would have mixed it into commits made since it was set aside, "
            + "and the two versions disagree. Your files have been put back exactly as they "
            + "were before this attempt, and the stash is still there — nothing was lost.",
            new[]
            {
                "Switch to the branch or commit the stash was originally set aside from, "
                + "then try again.",
                "Or open the file yourself to combine the two versions; this app does not "
                + "yet walk you through resolving a clash like this.",
            }),
    };

    public static TranslatedError? Translate(GitCommandResult result)
    {
        if (result.Success) return null;

        // git splits messages across both streams depending on the subcommand.
        var raw = (result.StdErr + "\n" + result.StdOut).Trim();

        foreach (var rule in Rules)
        {
            if (raw.Contains(rule.Pattern, StringComparison.OrdinalIgnoreCase))
                return new TranslatedError(
                    rule.Summary, rule.Explanation, rule.NextSteps, raw, IsUnderstood: true);
        }

        return new TranslatedError(
            Summary: "I don't have a plain-English explanation for this one",
            Explanation:
                "Git reported a problem this app does not recognise. The exact message is below — "
                + "searching the web for it usually finds an answer.",
            NextSteps: new[] { "Read the technical details below." },
            RawOutput: raw,
            IsUnderstood: false);
    }
}
