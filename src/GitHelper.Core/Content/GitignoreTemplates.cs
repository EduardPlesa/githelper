using System.Reflection;
using GitHelper.Core.Model;

namespace GitHelper.Core.Content;

/// <summary>
/// The shipped .gitignore templates, one per <see cref="ProjectType"/>. Loaded once: they are
/// embedded in the assembly and never change at runtime.
/// </summary>
public static class GitignoreTemplates
{
    private static readonly Lazy<IReadOnlyDictionary<ProjectType, string>> Templates =
        new(() => Load(global::GitHelper.Content.ContentAssembly.Value));

    public static string For(ProjectType type) => Templates.Value[type];

    private static IReadOnlyDictionary<ProjectType, string> Load(Assembly assembly)
    {
        var byType = new Dictionary<ProjectType, string>();

        foreach (var type in Enum.GetValues<ProjectType>())
        {
            // Resource names are dotted paths: GitHelper.Content.gitignore.dotnet.gitignore
            var suffix = $".gitignore.{type.ToString().ToLowerInvariant()}.gitignore";
            var name = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                ?? throw new ContentException($"No .gitignore template embedded for '{type}'.");

            using var stream = assembly.GetManifestResourceStream(name)
                ?? throw new ContentException($"{name}: embedded resource could not be opened.");
            using var reader = new StreamReader(stream);
            byType[type] = reader.ReadToEnd().Replace("\r\n", "\n");
        }

        return byType;
    }
}
