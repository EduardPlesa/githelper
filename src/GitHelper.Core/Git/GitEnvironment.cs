using System.ComponentModel;

namespace GitHelper.Core.Git;

/// <summary>Checks the machine can actually do what the app is about to offer.</summary>
public sealed class GitEnvironment(IGitRunner runner)
{
    public async Task<IReadOnlyList<EnvironmentCheck>> CheckAsync(CancellationToken ct = default)
    {
        var checks = new List<EnvironmentCheck>();
        var workingDirectory = Directory.GetCurrentDirectory();

        GitCommandResult? version = null;
        try
        {
            version = await runner.RunAsync(workingDirectory, new[] { "--version" }, ct);
        }
        catch (Win32Exception)
        {
            // The executable itself could not be started: git is not on PATH.
        }

        if (version is null || !version.Success)
        {
            checks.Add(new EnvironmentCheck(
                Id: "git-present",
                Status: CheckStatus.Blocking,
                Summary: "Git is not installed",
                Explanation:
                    "This app is a friendly front end for git, so git itself has to be on your "
                    + "computer. It is a free download and takes a couple of minutes to install.",
                FixHint: "Install Git for Windows from https://git-scm.com/download/win, then restart this app."));

            return checks;
        }

        checks.Add(new EnvironmentCheck(
            Id: "git-present",
            Status: CheckStatus.Ok,
            Summary: "Git is installed",
            Explanation: "Git was found on your computer.",
            FixHint: null));

        checks.Add(new EnvironmentCheck(
            Id: "git-version",
            Status: CheckStatus.Ok,
            Summary: version.StdOut.Trim(),
            Explanation: "The version of git this app is driving.",
            FixHint: null));

        checks.Add(await CheckIdentityAsync(workingDirectory, ct));
        return checks;
    }

    private async Task<EnvironmentCheck> CheckIdentityAsync(string workingDirectory, CancellationToken ct)
    {
        // git config exits non-zero when a key is unset, which is the signal used here.
        // --global mirrors SetIdentityAsync's write, so the check reflects what would change.
        var name = await runner.RunAsync(workingDirectory, new[] { "config", "--global", "user.name" }, ct);
        var email = await runner.RunAsync(workingDirectory, new[] { "config", "--global", "user.email" }, ct);

        var hasName = name.Success && name.StdOut.Trim().Length > 0;
        var hasEmail = email.Success && email.StdOut.Trim().Length > 0;

        if (hasName && hasEmail)
        {
            return new EnvironmentCheck(
                Id: "git-identity",
                Status: CheckStatus.Ok,
                Summary: $"Signing commits as {name.StdOut.Trim()}",
                Explanation: "Your name and email are set, so commits will be labelled with them.",
                FixHint: null);
        }

        return new EnvironmentCheck(
            Id: "git-identity",
            Status: CheckStatus.Warning,
            Summary: "Your name and email are not set yet",
            Explanation:
                "Every commit records who made it. Until this is set, your very first commit will "
                + "fail with a confusing message, so it is worth doing now. This is only a label — "
                + "it is not an account, and no password is involved.",
            FixHint: "Enter a name and email, and this app will save them for git.");
    }

    /// <summary>Writes the identity to the user's global git configuration.</summary>
    public async Task<GitCommandResult> SetIdentityAsync(
        string name, string email, CancellationToken ct = default)
    {
        var workingDirectory = Directory.GetCurrentDirectory();

        var nameResult = await runner.RunAsync(
            workingDirectory, new[] { "config", "--global", "user.name", name }, ct);
        if (!nameResult.Success) return nameResult;

        return await runner.RunAsync(
            workingDirectory, new[] { "config", "--global", "user.email", email }, ct);
    }

    public static bool IsUsable(IReadOnlyList<EnvironmentCheck> checks)
        => checks.All(c => c.Status != CheckStatus.Blocking);
}
