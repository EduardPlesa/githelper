using GitHelper.Core.Content;
using GitHelper.Core.Model;

namespace GitHelper.Core.Tests;

public class GitignoreTemplateTests
{
    [Theory]
    [InlineData(ProjectType.Generic)]
    [InlineData(ProjectType.DotNet)]
    [InlineData(ProjectType.Node)]
    [InlineData(ProjectType.Python)]
    [InlineData(ProjectType.Java)]
    public void EveryProjectTypeHasATemplate(ProjectType type)
    {
        var text = GitignoreTemplates.For(type);

        Assert.False(string.IsNullOrWhiteSpace(text));
    }

    [Theory]
    [InlineData(ProjectType.Generic)]
    [InlineData(ProjectType.DotNet)]
    [InlineData(ProjectType.Node)]
    [InlineData(ProjectType.Python)]
    [InlineData(ProjectType.Java)]
    public void EveryTemplateIsCommentedSoItCanBeExplained(ProjectType type)
    {
        var lines = GitignoreTemplates.For(type)
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

        Assert.Contains(lines, l => l.StartsWith('#'));
        // Every rule needs a comment somewhere above it; a wall of bare globs is unteachable.
        var rules = lines.Count(l => !l.StartsWith('#'));
        var comments = lines.Count(l => l.StartsWith('#'));
        Assert.True(comments >= rules / 3,
            $"{type}: {rules} rules but only {comments} comments");
    }

    [Fact]
    public void TheDotNetTemplateIgnoresBuildOutput()
    {
        var text = GitignoreTemplates.For(ProjectType.DotNet);

        Assert.Contains("bin/", text);
        Assert.Contains("obj/", text);
    }

    [Fact]
    public void TheNodeTemplateIgnoresDependencies()
    {
        Assert.Contains("node_modules/", GitignoreTemplates.For(ProjectType.Node));
    }

    [Fact]
    public void EveryTemplateIgnoresEnvironmentFiles()
    {
        // Committing a .env is the single most common way a beginner leaks a secret.
        foreach (var type in Enum.GetValues<ProjectType>())
            Assert.Contains(".env", GitignoreTemplates.For(type));
    }
}
