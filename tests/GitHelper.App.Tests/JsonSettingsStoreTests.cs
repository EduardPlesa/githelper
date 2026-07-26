using GitHelper.App.Settings;

namespace GitHelper.App.Tests;

public class AppSettingsTests
{
    [Fact]
    public void WithRepositoryOpened_PutsMostRecentFirst()
    {
        var settings = AppSettings.Default
            .WithRepositoryOpened(@"C:\a")
            .WithRepositoryOpened(@"C:\b");

        Assert.Equal(new[] { @"C:\b", @"C:\a" }, settings.RecentRepositories);
    }

    [Fact]
    public void WithRepositoryOpened_MovesAnExistingEntryToTheFrontWithoutDuplicating()
    {
        var settings = AppSettings.Default
            .WithRepositoryOpened(@"C:\a")
            .WithRepositoryOpened(@"C:\b")
            .WithRepositoryOpened(@"C:\a");

        Assert.Equal(new[] { @"C:\a", @"C:\b" }, settings.RecentRepositories);
    }

    [Fact]
    public void WithRepositoryOpened_CapsTheListAndDropsTheOldest()
    {
        var settings = AppSettings.Default;
        for (var i = 1; i <= AppSettings.MaxRecentRepositories + 3; i++)
            settings = settings.WithRepositoryOpened($@"C:\repo{i}");

        Assert.Equal(AppSettings.MaxRecentRepositories, settings.RecentRepositories.Count);
        Assert.Equal($@"C:\repo{AppSettings.MaxRecentRepositories + 3}", settings.RecentRepositories[0]);
        Assert.DoesNotContain(@"C:\repo1", settings.RecentRepositories);
    }

    [Fact]
    public void WithRepositoryRemoved_DropsOnlyThatEntry()
    {
        var settings = AppSettings.Default
            .WithRepositoryOpened(@"C:\a")
            .WithRepositoryOpened(@"C:\b")
            .WithRepositoryRemoved(@"C:\a");

        Assert.Equal(new[] { @"C:\b" }, settings.RecentRepositories);
    }

    [Fact]
    public void WithExplanationSuppressed_IsIdempotent()
    {
        var settings = AppSettings.Default
            .WithExplanationSuppressed("stage-file")
            .WithExplanationSuppressed("stage-file");

        Assert.Single(settings.SuppressedExplanations);
        Assert.Contains("stage-file", settings.SuppressedExplanations);
    }

    [Fact]
    public void Default_StartsEmptyAndFollowsTheSystemTheme()
    {
        Assert.Empty(AppSettings.Default.RecentRepositories);
        Assert.Empty(AppSettings.Default.SuppressedExplanations);
        Assert.Equal(AppTheme.System, AppSettings.Default.Theme);
    }
}

public class JsonSettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "githelper-settings-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
    }

    [Fact]
    public void Load_ReturnsDefaultsWhenTheFileDoesNotExist()
    {
        var store = new JsonSettingsStore(FilePath);

        var settings = store.Load();

        Assert.Empty(settings.RecentRepositories);
        Assert.Equal(AppTheme.System, settings.Theme);
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsEverything()
    {
        var store = new JsonSettingsStore(FilePath);
        var written = AppSettings.Default
            .WithRepositoryOpened(@"C:\repos\demo")
            .WithExplanationSuppressed("stage-file")
            .WithTheme(AppTheme.Dark);

        store.Save(written);
        var read = new JsonSettingsStore(FilePath).Load();

        Assert.Equal(new[] { @"C:\repos\demo" }, read.RecentRepositories);
        Assert.Contains("stage-file", read.SuppressedExplanations);
        Assert.Equal(AppTheme.Dark, read.Theme);
    }

    [Fact]
    public void Save_CreatesMissingDirectories()
    {
        var nested = Path.Combine(_dir, "deeper", "still", "settings.json");

        new JsonSettingsStore(nested).Save(AppSettings.Default.WithTheme(AppTheme.Light));

        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void Load_ReturnsDefaultsRatherThanThrowingOnCorruptJson()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ this is not json at all ");

        // A corrupt settings file must never stop the app from starting.
        var settings = new JsonSettingsStore(FilePath).Load();

        Assert.Equal(AppTheme.System, settings.Theme);
        Assert.Empty(settings.RecentRepositories);
    }

    [Fact]
    public void DefaultFilePath_SitsUnderLocalApplicationData()
    {
        var expectedRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.StartsWith(expectedRoot, JsonSettingsStore.DefaultFilePath);
        Assert.EndsWith("settings.json", JsonSettingsStore.DefaultFilePath);
    }
}
