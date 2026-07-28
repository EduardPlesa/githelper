using System.Text.RegularExpressions;
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

    [Theory]
    [InlineData(ProjectType.Generic)]
    [InlineData(ProjectType.DotNet)]
    [InlineData(ProjectType.Node)]
    [InlineData(ProjectType.Python)]
    [InlineData(ProjectType.Java)]
    public void EveryNegationRuleHasAPrecedingRuleThatCouldPlausiblyMatchIt(ProjectType type)
    {
        // A "!" line only means something if an earlier rule in the same file could actually
        // have matched the path first. Merely having *some* rule above it isn't enough - e.g.
        // ".gradle/" sits above "!gradle/wrapper/gradle-wrapper.jar" in a naive read but never
        // matches that path, so the negation would still be an unexplainable no-op.
        var rules = GitignoreTemplates.For(type)
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .ToList();

        var precedingRules = new List<string>();
        foreach (var rule in rules)
        {
            if (rule.StartsWith('!'))
            {
                var target = rule[1..];
                Assert.True(precedingRules.Any(r => CouldMatch(r, target)),
                    $"{type}: negation rule '{rule}' has no preceding rule that could plausibly match '{target}', so it un-ignores nothing.");
            }
            else
            {
                precedingRules.Add(rule);
            }
        }
    }

    // A deliberately simplified stand-in for gitignore's matching rules - enough to tell
    // "*.jar" apart from ".gradle/" when asking whether either could match
    // "gradle/wrapper/gradle-wrapper.jar". Not a full gitignore engine.
    private static bool CouldMatch(string rule, string targetPath)
    {
        var isDirectoryRule = rule.EndsWith('/');
        var pattern = isDirectoryRule ? rule[..^1] : rule;
        var regex = new Regex("^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$");

        if (pattern.Contains('/'))
        {
            // The rule names a specific path, not just a leaf name, so it must match in full.
            return regex.IsMatch(targetPath);
        }

        var segments = targetPath.Split('/');

        if (isDirectoryRule)
        {
            // A bare directory rule matches if any containing directory has that name.
            return segments.Take(segments.Length - 1).Any(s => regex.IsMatch(s));
        }

        // A bare file-glob rule matches against the file's own name, regardless of depth.
        return regex.IsMatch(segments[^1]);
    }
}
