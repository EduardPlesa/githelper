using GitHelper.Core.Model;

namespace GitHelper.Core.Parsing;

/// <summary>Parses the tag format produced by <see cref="Format"/>.</summary>
public static class TagParser
{
    /// <summary>
    /// A tab is a safe separator here: git rejects control characters in refnames, so no
    /// tag name can contain one.
    /// </summary>
    public const string Format = "%(refname:short)%09%(objectname:short)";

    public static IReadOnlyList<TagInfo> Parse(string output)
    {
        var tags = new List<TagInfo>();

        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.Length == 0) continue;

            var fields = trimmed.Split('\t');
            var name = fields[0];
            if (name.Length == 0) continue;

            var target = fields.Length > 1 ? fields[1] : string.Empty;
            tags.Add(new TagInfo(name, target));
        }

        return tags;
    }
}
