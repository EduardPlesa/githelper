using GitHelper.App.ViewModels;
using GitHelper.Core.Git;
using GitHelper.Core.Repo;

namespace GitHelper.App.Tests;

public class IdentitySetupTests
{
    /// <summary>A runner where git exists but the identity lookups report unset.</summary>
    private static StubGitRunner RunnerWithNoIdentity(List<IReadOnlyList<string>> calls) => new()
    {
        Respond = args =>
        {
            calls.Add(args);
            return args[0] switch
            {
                "--version" => new GitCommandResult(args, "git version 2.55.0", "", 0, TimeSpan.Zero),
                // Writing succeeds; reading reports unset via a non-zero exit.
                "config" when args.Contains("--global") && args.Count > 3
                    => new GitCommandResult(args, "", "", 0, TimeSpan.Zero),
                _ => new GitCommandResult(args, "", "", 1, TimeSpan.Zero),
            };
        },
    };

    private static StartupViewModel NewStartup(out List<IReadOnlyList<string>> calls)
    {
        calls = new List<IReadOnlyList<string>>();
        var runner = RunnerWithNoIdentity(calls);
        return new StartupViewModel(
            new InMemorySettingsStore(),
            new StubFolderPicker(),
            new RepoStateReader(runner),
            new GitEnvironment(runner));
    }

    [Fact]
    public async Task TheFormAppearsOnlyWhenTheIdentityIsMissing()
    {
        var startup = NewStartup(out _);

        await startup.InitializeAsync();

        Assert.True(startup.IdentityPromptNeeded);
    }

    [Fact]
    public async Task CannotSaveUntilBothFieldsAreFilled()
    {
        var startup = NewStartup(out _);
        await startup.InitializeAsync();

        Assert.False(startup.CanSaveIdentity);

        startup.IdentityName = "Ada Lovelace";
        Assert.False(startup.CanSaveIdentity);

        startup.IdentityEmail = "ada@example.com";
        Assert.True(startup.CanSaveIdentity);
    }

    [Fact]
    public async Task BlankOrWhitespaceOnlyValuesDoNotCount()
    {
        var startup = NewStartup(out _);
        await startup.InitializeAsync();

        startup.IdentityName = "   ";
        startup.IdentityEmail = "   ";

        Assert.False(startup.CanSaveIdentity);
    }

    [Fact]
    public async Task SaveIdentityCommand_WritesBothValuesGlobally()
    {
        var startup = NewStartup(out var calls);
        await startup.InitializeAsync();
        startup.IdentityName = "Ada Lovelace";
        startup.IdentityEmail = "ada@example.com";
        calls.Clear();

        await startup.SaveIdentityCommand.ExecuteAsync(null);

        Assert.Contains(calls, c => c.Contains("--global") && c.Contains("user.name") && c.Contains("Ada Lovelace"));
        Assert.Contains(calls, c => c.Contains("--global") && c.Contains("user.email") && c.Contains("ada@example.com"));
    }

    [Fact]
    public async Task SaveIdentityCommand_DismissesThePromptOnSuccess()
    {
        var startup = NewStartup(out _);
        await startup.InitializeAsync();
        startup.IdentityName = "Ada Lovelace";
        startup.IdentityEmail = "ada@example.com";

        await startup.SaveIdentityCommand.ExecuteAsync(null);

        Assert.False(startup.IdentityPromptNeeded);
        Assert.False(startup.HasIdentitySaveError);
    }

    [Fact]
    public async Task SaveIdentityCommand_ReportsAFailureInsteadOfSilentlySucceeding()
    {
        // git present, but every config write fails — e.g. a read-only global config.
        var runner = new StubGitRunner
        {
            Respond = args => args[0] switch
            {
                "--version" => new GitCommandResult(args, "git version 2.55.0", "", 0, TimeSpan.Zero),
                _ => new GitCommandResult(args, "", "could not lock config file", 255, TimeSpan.Zero),
            },
        };
        var startup = new StartupViewModel(
            new InMemorySettingsStore(), new StubFolderPicker(),
            new RepoStateReader(runner), new GitEnvironment(runner));
        await startup.InitializeAsync();
        startup.IdentityName = "Ada Lovelace";
        startup.IdentityEmail = "ada@example.com";

        await startup.SaveIdentityCommand.ExecuteAsync(null);

        Assert.True(startup.HasIdentitySaveError);
        Assert.False(string.IsNullOrWhiteSpace(startup.IdentitySaveError));
        // Still needed, because it was not actually set.
        Assert.True(startup.IdentityPromptNeeded);
    }

    [Fact]
    public async Task NoCredentialFieldExistsOnTheViewModel()
    {
        // Guards the app's standing rule: the identity is a label, never a login.
        var startup = NewStartup(out _);
        await startup.InitializeAsync();

        var suspicious = typeof(StartupViewModel)
            .GetProperties()
            .Where(p => p.Name.Contains("password", StringComparison.OrdinalIgnoreCase)
                     || p.Name.Contains("token", StringComparison.OrdinalIgnoreCase)
                     || p.Name.Contains("secret", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Name)
            .ToList();

        Assert.Empty(suspicious);
    }
}
