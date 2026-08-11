using GitHelper.Core.Git;

namespace GitHelper.Core.Repo;

/// <summary>
/// A read the repository state depends on failed, so no honest snapshot can be built.
///
/// This exists because the alternative is worse than an error. Every parser in this codebase
/// turns empty input into an empty result, so a failed <c>git status</c> would produce a
/// state with no branch, no commits and no changes — indistinguishable from a genuinely
/// empty repository. The user would be shown an empty project, and preconditions would be
/// evaluated against fiction.
/// </summary>
public sealed class GitReadException(GitCommandResult result)
    : Exception(Describe(result))
{
    /// <summary>The failed invocation, for the error panel and the command log.</summary>
    public GitCommandResult Result { get; } = result;

    private static string Describe(GitCommandResult result)
    {
        var detail = result.StdErr.Trim();
        if (detail.Length == 0) detail = $"exit code {result.ExitCode}";

        return $"Could not read this project's state: `{result.CommandLine}` failed. {detail}";
    }
}
