using GitHelper.App.Settings;
using GitHelper.App.ViewModels;
using GitHelper.Core.Git;
using GitHelper.Core.Repo;

namespace GitHelper.App.Tests;

public class StartupViewModelTests
{
    private sealed record Fixture(
        StartupViewModel Startup,
        InMemorySettingsStore Settings,
        StubFolderPicker Picker);

    /// <summary>Wires the viewmodel against real git, for the paths that need a real repo.</summary>
    private static Fixture NewRealFixture()
    {
        var runner = new GitRunner();
        var settings = new InMemorySettingsStore();
        var picker = new StubFolderPicker();
        var startup = new StartupViewModel(
            settings, picker, new RepoStateReader(runner), new GitEnvironment(runner));
        return new Fixture(startup, settings, picker);
    }

    [Fact]
    public async Task InitializeAsync_OffersTheChoiceWhenGitIsPresent()
    {
        var f = NewRealFixture();

        await f.Startup.InitializeAsync();

        Assert.Equal(StartupState.AwaitingChoice, f.Startup.State);
        Assert.Null(f.Startup.BlockingMessage);
    }

    [Fact]
    public async Task InitializeAsync_BlocksWithAnExplanationWhenGitIsNotInstalled()
    {
        var runner = new StubGitRunner { ThrowGitMissing = true };
        var startup = new StartupViewModel(
            new InMemorySettingsStore(), new StubFolderPicker(),
            new RepoStateReader(runner), new GitEnvironment(runner));

        await startup.InitializeAsync();

        Assert.Equal(StartupState.GitMissing, startup.State);
        Assert.False(string.IsNullOrWhiteSpace(startup.BlockingMessage));
        Assert.False(string.IsNullOrWhiteSpace(startup.BlockingFixHint));
    }

    [Fact]
    public async Task InitializeAsync_FlagsAMissingIdentityWithoutBlocking()
    {
        // git present, but both identity lookups exit non-zero, which is how git reports unset.
        var runner = new StubGitRunner
        {
            Respond = args => args[0] switch
            {
                "--version" => new GitCommandResult(args, "git version 2.55.0", "", 0, TimeSpan.Zero),
                _ => new GitCommandResult(args, "", "", 1, TimeSpan.Zero),
            },
        };
        var startup = new StartupViewModel(
            new InMemorySettingsStore(), new StubFolderPicker(),
            new RepoStateReader(runner), new GitEnvironment(runner));

        await startup.InitializeAsync();

        Assert.Equal(StartupState.AwaitingChoice, startup.State);
        Assert.True(startup.IdentityPromptNeeded);
    }

    [Fact]
    public async Task InitializeAsync_LoadsRecentsFromSettingsNewestFirst()
    {
        var f = NewRealFixture();
        f.Settings.Current = AppSettings.Default
            .WithRepositoryOpened(@"C:\repos\older")
            .WithRepositoryOpened(@"C:\repos\newer");

        await f.Startup.InitializeAsync();

        Assert.Equal(
            new[] { @"C:\repos\newer", @"C:\repos\older" },
            f.Startup.Recents.Select(r => r.FullPath));
    }

    [Fact]
    public async Task RecentEntriesShowTheFolderNameNotJustThePath()
    {
        var f = NewRealFixture();
        f.Settings.Current = AppSettings.Default.WithRepositoryOpened(@"C:\repos\my-project");

        await f.Startup.InitializeAsync();

        Assert.Equal("my-project", f.Startup.Recents.Single().Name);
    }

    [Fact]
    public async Task OpenAsync_RaisesRepositoryOpenedWithTheRepositoryRoot()
    {
        using var repo = await TestRepo.CreateAsync();
        var f = NewRealFixture();
        string? opened = null;
        f.Startup.RepositoryOpened += (_, path) => opened = path;

        await f.Startup.OpenAsync(repo.Path);

        Assert.NotNull(opened);
        Assert.Equal(Path.GetFileName(repo.Path), Path.GetFileName(opened!.TrimEnd('/', '\\')));
        Assert.Null(f.Startup.ErrorMessage);
    }

    [Fact]
    public async Task OpenAsync_RecordsTheResolvedRootWhenGivenASubdirectory()
    {
        using var repo = await TestRepo.CreateAsync();
        var nested = Path.Combine(repo.Path, "nested", "deeper");
        Directory.CreateDirectory(nested);
        var f = NewRealFixture();

        await f.Startup.OpenAsync(nested);

        // The recents entry must reopen the project, not the subfolder.
        var recorded = f.Settings.Current.RecentRepositories.Single();
        Assert.Equal(Path.GetFileName(repo.Path), Path.GetFileName(recorded.TrimEnd('/', '\\')));
    }

    [Fact]
    public async Task OpenAsync_ExplainsWhenTheFolderIsNotAGitProject()
    {
        var dir = Path.Combine(Path.GetTempPath(), "githelper-notarepo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var f = NewRealFixture();
        var raised = false;
        f.Startup.RepositoryOpened += (_, _) => raised = true;

        try
        {
            await f.Startup.OpenAsync(dir);

            Assert.False(raised);
            Assert.False(string.IsNullOrWhiteSpace(f.Startup.ErrorMessage));
            Assert.Empty(f.Settings.Current.RecentRepositories);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task BrowseCommand_OpensWhatThePickerReturns()
    {
        using var repo = await TestRepo.CreateAsync();
        var f = NewRealFixture();
        f.Picker.NextResult = repo.Path;
        string? opened = null;
        f.Startup.RepositoryOpened += (_, path) => opened = path;

        await f.Startup.BrowseCommand.ExecuteAsync(null);

        Assert.Equal(1, f.Picker.CallCount);
        Assert.NotNull(opened);
    }

    [Fact]
    public async Task BrowseCommand_DoesNothingWhenTheUserCancels()
    {
        var f = NewRealFixture();
        f.Picker.NextResult = null;
        var raised = false;
        f.Startup.RepositoryOpened += (_, _) => raised = true;

        await f.Startup.BrowseCommand.ExecuteAsync(null);

        Assert.False(raised);
        Assert.Null(f.Startup.ErrorMessage);
    }

    [Fact]
    public async Task RemoveCommand_DropsAStaleRecentFromBothTheListAndSettings()
    {
        var f = NewRealFixture();
        f.Settings.Current = AppSettings.Default
            .WithRepositoryOpened(@"C:\repos\keep")
            .WithRepositoryOpened(@"C:\repos\drop");
        await f.Startup.InitializeAsync();

        f.Startup.Recents.Single(r => r.FullPath == @"C:\repos\drop").RemoveCommand.Execute(null);

        Assert.Equal(new[] { @"C:\repos\keep" }, f.Startup.Recents.Select(r => r.FullPath));
        Assert.Equal(new[] { @"C:\repos\keep" }, f.Settings.Current.RecentRepositories);
    }

    [Fact]
    public async Task OpenAsync_MovesAnAlreadyKnownRepositoryToTheFrontOfRecents()
    {
        using var repo = await TestRepo.CreateAsync();
        var f = NewRealFixture();
        f.Settings.Current = AppSettings.Default.WithRepositoryOpened(@"C:\repos\other");

        await f.Startup.OpenAsync(repo.Path);

        Assert.Equal(2, f.Settings.Current.RecentRepositories.Count);
        Assert.Equal(
            Path.GetFileName(repo.Path),
            Path.GetFileName(f.Settings.Current.RecentRepositories[0].TrimEnd('/', '\\')));
    }
}
