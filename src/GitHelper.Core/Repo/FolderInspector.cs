using GitHelper.Core.Model;

namespace GitHelper.Core.Repo;

/// <summary>
/// Reads a folder without running git. Pure enough to run on every refresh, and testable
/// against a temp directory with no repository involved.
/// </summary>
public sealed class FolderInspector
{
    public FolderState Inspect(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            return new FolderState(folderPath, false, 0, false, ProjectType.Generic);

        var isRepository = Directory.Exists(Path.Combine(folderPath, ".git"));
        var names = SafeList(folderPath);

        return new FolderState(
            Path: folderPath,
            IsRepository: isRepository,
            FileCount: names.Count,
            HasGitignore: File.Exists(Path.Combine(folderPath, ".gitignore")),
            ProjectType: Detect(names));
    }

    /// <summary>
    /// Files at the root plus one level down. Solutions routinely keep every project in a
    /// subfolder, leaving nothing telling at the root — and going deeper would mean walking
    /// node_modules on every refresh.
    /// </summary>
    private static List<string> SafeList(string folderPath)
    {
        var names = new List<string>();

        try
        {
            foreach (var file in Directory.EnumerateFiles(folderPath))
                names.Add(Path.GetFileName(file));

            foreach (var directory in Directory.EnumerateDirectories(folderPath))
            {
                if (string.Equals(Path.GetFileName(directory), ".git", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    foreach (var file in Directory.EnumerateFiles(directory))
                        names.Add(Path.GetFileName(file));
                }
                catch (UnauthorizedAccessException)
                {
                    // A folder we cannot read, or one that vanishes mid-scan (deleted,
                    // disconnected network share, broken junction), tells us nothing about the
                    // rest of the tree; it must not abort the scan of its still-live siblings.
                }
                catch (IOException)
                {
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }

        return names;
    }

    private static ProjectType Detect(IReadOnlyList<string> names)
    {
        // Ordered by how conclusive the marker is. A csproj means .NET even if a stray .py
        // sits beside it.
        if (names.Any(n => n.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                        || n.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
                        || n.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase)))
            return ProjectType.DotNet;

        if (names.Any(n => n.Equals("package.json", StringComparison.OrdinalIgnoreCase)))
            return ProjectType.Node;

        if (names.Any(n => n.Equals("pom.xml", StringComparison.OrdinalIgnoreCase)
                        || n.StartsWith("build.gradle", StringComparison.OrdinalIgnoreCase)))
            return ProjectType.Java;

        if (names.Any(n => n.Equals("requirements.txt", StringComparison.OrdinalIgnoreCase)
                        || n.Equals("pyproject.toml", StringComparison.OrdinalIgnoreCase)
                        || n.EndsWith(".py", StringComparison.OrdinalIgnoreCase)))
            return ProjectType.Python;

        return ProjectType.Generic;
    }
}
