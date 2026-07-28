using System.Reflection;

namespace GitHelper.Core.Content;

/// <summary>Loads and indexes every embedded content file.</summary>
public sealed class ContentLibrary
{
    public IReadOnlyDictionary<string, ExplanationDocument> Actions { get; }
    public IReadOnlyDictionary<string, ExplanationDocument> Setup { get; }
    public IReadOnlyDictionary<string, GlossaryTerm> Terms { get; }

    private ContentLibrary(
        IReadOnlyDictionary<string, ExplanationDocument> actions,
        IReadOnlyDictionary<string, ExplanationDocument> setup,
        IReadOnlyDictionary<string, GlossaryTerm> terms)
    {
        Actions = actions;
        Setup = setup;
        Terms = terms;
    }

    // Fully qualified: this type sits in GitHelper.Core.Content, so an unqualified
    // "Content.ContentAssembly" would bind to the wrong namespace.
    public static ContentLibrary Load() => Load(global::GitHelper.Content.ContentAssembly.Value);

    public static ContentLibrary Load(Assembly assembly)
    {
        var actions = new Dictionary<string, ExplanationDocument>(StringComparer.OrdinalIgnoreCase);
        var setup = new Dictionary<string, ExplanationDocument>(StringComparer.OrdinalIgnoreCase);
        var terms = new Dictionary<string, GlossaryTerm>(StringComparer.OrdinalIgnoreCase);

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) continue;

            var text = ReadResource(assembly, resourceName);

            // Resource names are dotted paths such as GitHelper.Content.actions.stage_file.md
            if (resourceName.Contains(".actions.", StringComparison.OrdinalIgnoreCase))
            {
                var document = ContentParser.Parse(text, resourceName);
                if (actions.ContainsKey(document.Id))
                    throw new ContentException($"{resourceName}: duplicate action id '{document.Id}'.");
                actions[document.Id] = document;
            }
            else if (resourceName.Contains(".setup.", StringComparison.OrdinalIgnoreCase))
            {
                var document = ContentParser.Parse(text, resourceName);
                if (setup.ContainsKey(document.Id))
                    throw new ContentException($"{resourceName}: duplicate setup id '{document.Id}'.");
                setup[document.Id] = document;
            }
            else if (resourceName.Contains(".terms.", StringComparison.OrdinalIgnoreCase))
            {
                var term = ParseTerm(text, resourceName);
                if (terms.ContainsKey(term.Id))
                    throw new ContentException($"{resourceName}: duplicate term id '{term.Id}'.");
                terms[term.Id] = term;
            }
        }

        return new ContentLibrary(actions, setup, terms);
    }

    private static string ReadResource(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new ContentException($"{name}: embedded resource could not be opened.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Glossary files share the frontmatter format but have a single '## definition'
    /// section, so they are parsed here rather than by the action-shaped ContentParser.
    /// </summary>
    private static GlossaryTerm ParseTerm(string text, string sourceName)
    {
        var normalized = text.Replace("\r\n", "\n");

        string? id = null;
        string? title = null;
        var marker = "\n---";
        var end = normalized.IndexOf(marker, 3, StringComparison.Ordinal);
        if (!normalized.StartsWith("---\n", StringComparison.Ordinal) || end < 0)
            throw new ContentException($"{sourceName}: term file must begin with '---' frontmatter.");

        foreach (var line in normalized[4..end].Split('\n'))
        {
            var separator = line.IndexOf(':');
            if (separator < 0) continue;
            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (key.Equals("id", StringComparison.OrdinalIgnoreCase)) id = value;
            else if (key.Equals("title", StringComparison.OrdinalIgnoreCase)) title = value;
        }

        if (string.IsNullOrWhiteSpace(id))
            throw new ContentException($"{sourceName}: term frontmatter is missing 'id'.");
        if (string.IsNullOrWhiteSpace(title))
            throw new ContentException($"{sourceName}: term frontmatter is missing 'title'.");

        var bodyStart = normalized.IndexOf('\n', end + 1);
        var body = bodyStart < 0 ? string.Empty : normalized[(bodyStart + 1)..];

        var headingIndex = body.IndexOf("## definition", StringComparison.OrdinalIgnoreCase);
        if (headingIndex < 0)
            throw new ContentException($"{sourceName}: missing required section '## definition'.");

        var definitionText = body[(headingIndex + "## definition".Length)..];
        var definition = ContentParser.ParseBlocksForTerm(definitionText);
        if (definition.Count == 0)
            throw new ContentException($"{sourceName}: '## definition' section is empty.");

        return new GlossaryTerm(id!, title!, definition);
    }
}
