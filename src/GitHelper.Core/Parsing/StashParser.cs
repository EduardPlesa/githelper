using GitHelper.Core.Model;

namespace GitHelper.Core.Parsing;

/// <summary>Parses the stash list format produced by <see cref="Format"/>.</summary>
public static class StashParser
{
    /// <summary>
    /// %gd is the reflog selector (e.g. "stash@{0}") and is what every stash action passes
    /// straight back to git, never re-derived from the entry's position. %s is the stash's
    /// own one-line subject. Only the first tab is treated as the field separator, because
    /// the subject is freeform text that could in principle contain one of its own.
    /// </summary>
    public const string Format = "%gd%x09%s";

    public static IReadOnlyList<StashInfo> Parse(string output)
    {
        var stashes = new List<StashInfo>();

        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.Length == 0) continue;

            var tab = trimmed.IndexOf('\t');
            if (tab < 0) continue;

            var reference = trimmed[..tab];
            if (reference.Length == 0) continue;

            stashes.Add(new StashInfo(reference, trimmed[(tab + 1)..]));
        }

        return stashes;
    }
}
