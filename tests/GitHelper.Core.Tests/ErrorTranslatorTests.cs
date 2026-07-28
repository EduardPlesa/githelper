using GitHelper.Core.Errors;
using GitHelper.Core.Git;

namespace GitHelper.Core.Tests;

public class ErrorTranslatorTests
{
    private static GitCommandResult Failure(string stdErr)
        => new(new[] { "push" }, StdOut: "", StdErr: stdErr, ExitCode: 1, Duration: TimeSpan.Zero);

    [Fact]
    public void Translate_ReturnsNullWhenTheCommandSucceeded()
    {
        var success = new GitCommandResult(new[] { "status" }, "", "", 0, TimeSpan.Zero);

        Assert.Null(ErrorTranslator.Translate(success));
    }

    [Theory]
    [InlineData("! [rejected]        main -> main (non-fast-forward)", "rejected")]
    [InlineData("fatal: The current branch main has no upstream branch.", "upstream")]
    [InlineData("fatal: not a git repository (or any of the parent directories): .git", "git project")]
    [InlineData("error: Your local changes to the following files would be overwritten by checkout:", "overwrite")]
    [InlineData("fatal: Authentication failed for 'https://example.com/repo.git/'", "sign in")]
    [InlineData("error: The branch 'feature' is not fully merged.", "nowhere else")]
    [InlineData("fatal: 'origin' does not appear to be a git repository", "server")]
    [InlineData("fatal: Not possible to fast-forward, aborting.", "fast-forward")]
    // Real git output, verbatim: what a beginner hits committing in a freshly `git init`-ed
    // repository with no global identity set.
    [InlineData("*** Please tell me who you are.\n\nfatal: unable to auto-detect email address", "who you are")]
    public void Translate_RecognisesKnownFailures(string stdErr, string expectedFragment)
    {
        var translated = ErrorTranslator.Translate(Failure(stdErr))!;

        Assert.True(translated.IsUnderstood);
        Assert.Contains(
            expectedFragment,
            translated.Summary + " " + translated.Explanation,
            StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(translated.NextSteps);
    }

    [Fact]
    public void Translate_AlwaysKeepsTheRawOutputReachable()
    {
        const string raw = "fatal: The current branch main has no upstream branch.";

        var translated = ErrorTranslator.Translate(Failure(raw))!;

        Assert.Contains(raw, translated.RawOutput);
    }

    [Fact]
    public void Translate_AdmitsIgnoranceRatherThanGuessing()
    {
        var translated = ErrorTranslator.Translate(Failure("fatal: something nobody has ever seen"))!;

        Assert.False(translated.IsUnderstood);
        Assert.Contains("plain-english", translated.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("something nobody has ever seen", translated.RawOutput);
    }

    [Fact]
    public void Translate_ReadsMessagesGitWroteToStdOut()
    {
        // git pull reports some refusals on stdout rather than stderr.
        var result = new GitCommandResult(
            new[] { "pull" },
            StdOut: "fatal: Not possible to fast-forward, aborting.",
            StdErr: "",
            ExitCode: 128,
            Duration: TimeSpan.Zero);

        Assert.True(ErrorTranslator.Translate(result)!.IsUnderstood);
    }

    [Fact]
    public void Translate_NamesTheReadmeTrapWhenAFirstSendIsRejected()
    {
        // Real git output when the GitHub repository was created with "Add a README" ticked.
        var translated = ErrorTranslator.Translate(Failure(
            " ! [rejected]        main -> main (fetch first)\n"
            + "error: failed to push some refs to 'https://github.com/me/project.git'"))!;

        Assert.True(translated.IsUnderstood);
        Assert.Contains(
            "README",
            translated.Summary + " " + translated.Explanation,
            StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(translated.NextSteps);
    }

    [Fact]
    public void Translate_StillBlamesTheOtherPersonForAnOrdinaryNonFastForward()
    {
        // The general rule must survive the more specific one being added ahead of it.
        var translated = ErrorTranslator.Translate(Failure(
            " ! [rejected]        main -> main (non-fast-forward)"))!;

        Assert.Contains("someone else", translated.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Translate_SaysTheAddressCanBeChangedWhenTheRepositoryIsNotThere()
    {
        var translated = ErrorTranslator.Translate(Failure(
            "remote: Repository not found.\n"
            + "fatal: repository 'https://github.com/me/typo.git/' not found"))!;

        Assert.True(translated.IsUnderstood);
        Assert.Contains(
            translated.NextSteps,
            step => step.Contains("disconnect", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EveryRuleProducesNonEmptyUserFacingCopy()
    {
        string[] samples =
        {
            "! [rejected] main -> main (non-fast-forward)",
            "fatal: The current branch main has no upstream branch.",
            "fatal: not a git repository",
            "error: Your local changes to the following files would be overwritten by checkout:",
            "fatal: Authentication failed",
            "error: The branch 'feature' is not fully merged.",
            "fatal: 'origin' does not appear to be a git repository",
            "fatal: Not possible to fast-forward, aborting.",
            "nothing to commit, working tree clean",
        };

        foreach (var sample in samples)
        {
            var translated = ErrorTranslator.Translate(Failure(sample))!;
            Assert.False(string.IsNullOrWhiteSpace(translated.Summary));
            Assert.False(string.IsNullOrWhiteSpace(translated.Explanation));
        }
    }
}
