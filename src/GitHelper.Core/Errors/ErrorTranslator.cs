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

        new("does not appear to be a git repository",
            "The server address does not work",
            "Git could not find a project at the address configured for this remote.",
            new[] { "Check the project address in your git settings." }),

        new("not possible to fast-forward",
            "Both you and the server have new work",
            "A fast-forward only works when you have made nothing new. Since both sides have "
            + "commits, the two histories have to be combined, which this app does not do yet.",
            new[]
            {
                "Save or set aside your local commits.",
                "Combining histories is not supported in this version.",
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
