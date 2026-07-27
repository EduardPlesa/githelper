using GitHelper.Core.Model;
using GitHelper.Core.Repo;

namespace GitHelper.Core.Tests;

public class FolderInspectorTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "githelper-inspect-" + Guid.NewGuid().ToString("N"));

    public FolderInspectorTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private void Write(string name, string content = "x")
    {
        var full = Path.Combine(_dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    [Fact]
    public void AnEmptyFolderIsNotARepositoryAndHasNoFiles()
    {
        var state = new FolderInspector().Inspect(_dir);

        Assert.False(state.IsRepository);
        Assert.Equal(0, state.FileCount);
        Assert.False(state.HasGitignore);
        Assert.Equal(ProjectType.Generic, state.ProjectType);
    }

    [Fact]
    public void ADotGitDirectoryMakesItARepository()
    {
        Directory.CreateDirectory(Path.Combine(_dir, ".git"));

        Assert.True(new FolderInspector().Inspect(_dir).IsRepository);
    }

    [Fact]
    public void AnExistingGitignoreIsReported()
    {
        Write(".gitignore", "bin/\n");

        Assert.True(new FolderInspector().Inspect(_dir).HasGitignore);
    }

    [Theory]
    [InlineData("App.csproj", ProjectType.DotNet)]
    [InlineData("package.json", ProjectType.Node)]
    [InlineData("main.py", ProjectType.Python)]
    [InlineData("pom.xml", ProjectType.Java)]
    [InlineData("build.gradle", ProjectType.Java)]
    [InlineData("notes.txt", ProjectType.Generic)]
    public void ProjectTypeIsDetectedFromTheFilesPresent(string fileName, ProjectType expected)
    {
        Write(fileName);

        Assert.Equal(expected, new FolderInspector().Inspect(_dir).ProjectType);
    }

    [Fact]
    public void DetectionLooksOneLevelDownAsWell()
    {
        // Solutions commonly keep projects in subfolders, with nothing telling at the root.
        Write("App/App.csproj");

        Assert.Equal(ProjectType.DotNet, new FolderInspector().Inspect(_dir).ProjectType);
    }

    [Fact]
    public void FileCountIgnoresTheGitDirectory()
    {
        // .git holds hundreds of files; counting them would tell the user nothing true.
        Directory.CreateDirectory(Path.Combine(_dir, ".git"));
        Write(".git/config", "[core]\n");
        Write("readme.txt");

        Assert.Equal(1, new FolderInspector().Inspect(_dir).FileCount);
    }

    [Fact]
    public void AMissingFolderIsReportedAsEmptyRatherThanThrowing()
    {
        var missing = Path.Combine(_dir, "nope");

        var state = new FolderInspector().Inspect(missing);

        Assert.False(state.IsRepository);
        Assert.Equal(0, state.FileCount);
    }
}
