using System.Text.RegularExpressions;
using GitHelper.Core.Model;

namespace GitHelper.Core.Parsing;

/// <summary>
/// Parses git's unified diff for a single file. Deliberately a parser rather than
/// colour-by-first-character: the header's own '---' and '+++' lines would read as a removed
/// and an added line under that rule, and nothing would carry line numbers.
/// </summary>
public static partial class DiffParser
{
    /// <summary>
    /// Where a diff stops being something to read and becomes something to grep. Named for
    /// the same reason RepoStateReader.RecentCommitLimit is, rather than buried in a method.
    /// </summary>
    public const int MaxLines = 2000;

    public static FileDiff Parse(string path, string output)
    {
        var empty = new FileDiff(path, DiffKind.Empty, Array.Empty<DiffHunk>(), Truncated: false);
        if (string.IsNullOrWhiteSpace(output)) return empty;

        var lines = output.Replace("\r\n", "\n").Split('\n');

        if (lines.Any(IsBinaryMarker))
            return new FileDiff(path, DiffKind.Binary, Array.Empty<DiffHunk>(), Truncated: false);

        var hunks = new List<DiffHunk>();
        var current = new List<DiffLine>();
        DiffHunk? header = null;
        var oldNumber = 0;
        var newNumber = 0;
        var emitted = 0;
        var truncated = false;

        void CloseHunk()
        {
            if (header is null) return;
            hunks.Add(header with { Lines = current.ToArray() });
            header = null;
            current = new List<DiffLine>();
        }

        foreach (var line in lines)
        {
            var match = HunkHeader().Match(line);
            if (match.Success)
            {
                CloseHunk();
                oldNumber = int.Parse(match.Groups["oldStart"].Value);
                newNumber = int.Parse(match.Groups["newStart"].Value);
                header = new DiffHunk(
                    Header: line,
                    OldStart: oldNumber,
                    OldCount: Count(match.Groups["oldCount"]),
                    NewStart: newNumber,
                    NewCount: Count(match.Groups["newCount"]),
                    Lines: Array.Empty<DiffLine>());
                continue;
            }

            // Everything before the first hunk header is file metadata: 'diff --git', 'index',
            // '--- a/x', '+++ b/x', mode and rename lines. None of it is a changed line.
            if (header is null) continue;

            if (emitted == MaxLines)
            {
                truncated = true;
                break;
            }

            if (line.Length == 0) continue;

            DiffLine parsed;
            switch (line[0])
            {
                case ' ':
                    parsed = new DiffLine(DiffLineKind.Context, line[1..], oldNumber++, newNumber++);
                    break;
                case '+':
                    parsed = new DiffLine(DiffLineKind.Added, line[1..], null, newNumber++);
                    break;
                case '-':
                    parsed = new DiffLine(DiffLineKind.Removed, line[1..], oldNumber++, null);
                    break;
                case '\\':
                    parsed = new DiffLine(DiffLineKind.NoNewlineMarker, line[2..], null, null);
                    break;
                default:
                    // A line git's format does not define. Skipped rather than guessed at.
                    continue;
            }

            current.Add(parsed);
            emitted++;
        }

        CloseHunk();

        // No hunks at all is a real answer, not a parse failure: a rename with no edits
        // produces headers and nothing else.
        return hunks.Count == 0
            ? empty
            : new FileDiff(path, DiffKind.Text, hunks, truncated);
    }

    private static int Count(Group group)
        => group.Success ? int.Parse(group.Value) : 1;

    private static bool IsBinaryMarker(string line)
        => (line.StartsWith("Binary files ", StringComparison.Ordinal)
                && line.EndsWith(" differ", StringComparison.Ordinal))
            || line.StartsWith("GIT binary patch", StringComparison.Ordinal);

    // Counts are optional: git writes "@@ -1 +1 @@" when a hunk is one line long.
    [GeneratedRegex(@"^@@ -(?<oldStart>\d+)(,(?<oldCount>\d+))? \+(?<newStart>\d+)(,(?<newCount>\d+))? @@")]
    private static partial Regex HunkHeader();
}
