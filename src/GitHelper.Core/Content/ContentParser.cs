using System.Text;
using System.Text.RegularExpressions;
using GitHelper.Core.Actions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace GitHelper.Core.Content;

/// <summary>Parses an authored content file into <see cref="ExplanationDocument"/>.</summary>
public static partial class ContentParser
{
    private static readonly string[] RequiredSections = { "what", "risks", "undo" };

    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private sealed class Frontmatter
    {
        public string? Id { get; set; }
        public string? Title { get; set; }
        public string? Danger { get; set; }
        public List<string>? Terms { get; set; }
        public string? Undo { get; set; }
    }

    public static ExplanationDocument Parse(string fileText, string sourceName)
    {
        var (frontmatterText, body) = SplitFrontmatter(fileText, sourceName);

        Frontmatter matter;
        try
        {
            matter = Yaml.Deserialize<Frontmatter>(frontmatterText) ?? new Frontmatter();
        }
        catch (Exception ex)
        {
            throw new ContentException($"{sourceName}: frontmatter is not valid YAML. {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(matter.Id))
            throw new ContentException($"{sourceName}: frontmatter is missing 'id'.");
        if (string.IsNullOrWhiteSpace(matter.Title))
            throw new ContentException($"{sourceName}: frontmatter is missing 'title'.");
        if (!Enum.TryParse<Danger>(matter.Danger, ignoreCase: true, out var danger))
            throw new ContentException(
                $"{sourceName}: frontmatter 'danger' must be safe, caution, or destructive.");

        var sections = SplitSections(body, sourceName);

        foreach (var required in RequiredSections)
        {
            if (!sections.ContainsKey(required))
                throw new ContentException($"{sourceName}: missing required section '## {required}'.");
        }

        return new ExplanationDocument(
            Id: matter.Id!,
            Title: matter.Title!,
            Danger: danger,
            Terms: matter.Terms ?? new List<string>(),
            UndoActionId: string.IsNullOrWhiteSpace(matter.Undo) ? null : matter.Undo,
            What: ParseBlocks(sections["what"]),
            Risks: ParseBlocks(sections["risks"]),
            Undo: ParseBlocks(sections["undo"]));
    }

    private static (string Frontmatter, string Body) SplitFrontmatter(string fileText, string sourceName)
    {
        var normalized = fileText.Replace("\r\n", "\n");
        if (!normalized.StartsWith("---\n", StringComparison.Ordinal))
            throw new ContentException($"{sourceName}: file must begin with '---' frontmatter.");

        var end = normalized.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (end < 0)
            throw new ContentException($"{sourceName}: frontmatter is not closed with '---'.");
        if (end < 4)
            throw new ContentException($"{sourceName}: frontmatter is empty.");

        var frontmatter = normalized[4..end];
        var afterMarker = normalized.IndexOf('\n', end + 1);
        var body = afterMarker < 0 ? string.Empty : normalized[(afterMarker + 1)..];
        return (frontmatter, body);
    }

    private static Dictionary<string, string> SplitSections(string body, string sourceName)
    {
        var sections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? current = null;
        var buffer = new StringBuilder();

        void Flush()
        {
            if (current is not null) sections[current] = buffer.ToString();
            buffer.Clear();
        }

        foreach (var line in body.Split('\n'))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                Flush();
                current = line[3..].Trim();
                if (!RequiredSections.Contains(current, StringComparer.OrdinalIgnoreCase))
                    throw new ContentException(
                        $"{sourceName}: unknown section '## {current}'. Allowed: what, risks, undo.");
                continue;
            }

            if (current is not null) buffer.Append(line).Append('\n');
        }

        Flush();
        return sections;
    }

    private static IReadOnlyList<ContentBlock> ParseBlocks(string section)
    {
        var blocks = new List<ContentBlock>();
        var lines = section.Split('\n');
        var i = 0;

        while (i < lines.Length)
        {
            var line = lines[i];

            if (line.Trim().Length == 0)
            {
                i++;
                continue;
            }

            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                i++;
                var code = new List<string>();
                while (i < lines.Length && !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
                    code.Add(lines[i++]);
                i++; // closing fence
                blocks.Add(new CodeBlock(string.Join('\n', code).Trim('\n')));
                continue;
            }

            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                var items = new List<IReadOnlyList<InlineSpan>>();
                while (i < lines.Length && lines[i].StartsWith("- ", StringComparison.Ordinal))
                    items.Add(ParseInline(lines[i++][2..]));
                blocks.Add(new BulletListBlock(items));
                continue;
            }

            var paragraph = new List<string>();
            while (i < lines.Length
                   && lines[i].Trim().Length > 0
                   && !lines[i].StartsWith("- ", StringComparison.Ordinal)
                   && !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                paragraph.Add(lines[i++].Trim());
            }

            blocks.Add(new ParagraphBlock(ParseInline(string.Join(' ', paragraph))));
        }

        return blocks;
    }

    // Matches, in one pass: `code`, [[term]] or [[term|display]], and {slot}.
    [GeneratedRegex(@"`(?<code>[^`]+)`|\[\[(?<term>[^\]|]+)(\|(?<display>[^\]]+))?\]\]|\{(?<slot>[A-Za-z][A-Za-z0-9]*)\}")]
    private static partial Regex InlinePattern();

    private static IReadOnlyList<InlineSpan> ParseInline(string text)
    {
        var spans = new List<InlineSpan>();
        var position = 0;

        foreach (Match match in InlinePattern().Matches(text))
        {
            if (match.Index > position)
                spans.Add(new TextSpan(text[position..match.Index]));

            if (match.Groups["code"].Success)
            {
                spans.Add(new CodeSpan(match.Groups["code"].Value));
            }
            else if (match.Groups["term"].Success)
            {
                var id = match.Groups["term"].Value;
                var display = match.Groups["display"].Success ? match.Groups["display"].Value : id;
                spans.Add(new TermSpan(id, display));
            }
            else
            {
                spans.Add(new SlotSpan(match.Groups["slot"].Value));
            }

            position = match.Index + match.Length;
        }

        if (position < text.Length)
            spans.Add(new TextSpan(text[position..]));

        return spans;
    }
}
