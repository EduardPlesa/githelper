using GitHelper.App.ViewModels;
using GitHelper.Core.Git;
using GitHelper.Core.Repo;

namespace GitHelper.App.Tests;

public class StartupInitOfferTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "githelper-offer-" + Guid.NewGuid().ToString("N"));

    public StartupInitOfferTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static StartupViewModel NewStartup()
    {
        var runner = new GitRunner();
        return new StartupViewModel(
            new InMemorySettingsStore(),
            new StubFolderPicker(),
            new RepoStateReader(runner),
            new GitEnvironment(runner),
            new FolderInspector());
    }

    [Fact]
    public async Task ANonRepositoryFolderBecomesAnOfferRatherThanADeadEnd()
    {
        var startup = NewStartup();
        await startup.InitializeAsync();

        await startup.OpenAsync(_dir);

        Assert.True(startup.IsOfferingInit);
        Assert.Equal(StartupState.FolderIsNotARepository, startup.State);
        Assert.NotNull(startup.PendingFolder);
        Assert.Equal(_dir, startup.PendingFolder!.Path);
    }

    [Fact]
    public async Task TheSummaryDistinguishesAnEmptyFolderFromOneWithFiles()
    {
        var startup = NewStartup();
        await startup.InitializeAsync();

        await startup.OpenAsync(_dir);
        Assert.Contains("empty", startup.PendingFolderSummary, StringComparison.OrdinalIgnoreCase);

        File.WriteAllText(Path.Combine(_dir, "a.txt"), "x");
        File.WriteAllText(Path.Combine(_dir, "b.txt"), "x");
        await startup.OpenAsync(_dir);
        Assert.Contains("2", startup.PendingFolderSummary);
    }

    [Fact]
    public async Task AcceptingTheOfferRaisesInitRequestedWithTheFolder()
    {
        var startup = NewStartup();
        await startup.InitializeAsync();
        await startup.OpenAsync(_dir);
        string? requested = null;
        startup.InitRequestedAsync = (path, _) => { requested = path; return Task.CompletedTask; };

        await startup.StartTrackingCommand.ExecuteAsync(null);

        Assert.Equal(_dir, requested);
    }

    [Fact]
    public async Task TheOfferIsNotAddedToRecentProjects()
    {
        // It is not a project yet. Recording it would offer a dead entry on the next launch.
        var startup = NewStartup();
        await startup.InitializeAsync();

        await startup.OpenAsync(_dir);

        Assert.Empty(startup.Recents);
    }

    [Fact]
    public async Task OpeningARealRepositoryStillWorks()
    {
        using var repo = await TestRepo.CreateAsync();
        var startup = NewStartup();
        await startup.InitializeAsync();
        string? opened = null;
        startup.RepositoryOpenedAsync = (path, _) => { opened = path; return Task.CompletedTask; };

        await startup.OpenAsync(repo.Path);

        Assert.False(startup.IsOfferingInit);
        Assert.NotNull(opened);
    }
}
