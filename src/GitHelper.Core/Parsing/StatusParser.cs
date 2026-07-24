using GitHelper.Core.Model;

namespace GitHelper.Core.Parsing;

/// <summary>Parses <c>git status --porcelain=v2 -z --branch</c>.</summary>
public static class StatusParser
{
    public static StatusSnapshot Parse(string output)
    {
        string? branch = null;
        var isDetached = false;
        var hasCommits = false;
        string? upstream = null;
        var ahead = 0;
        var behind = 0;
        var changes = new List<FileChange>();

        // Records are NUL-terminated, so the final split element is an empty tail.
        var records = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < records.Length; i++)
        {
            var record = records[i];
            if (record.Length == 0) continue;

            if (record[0] == '#')
            {
                ParseHeader(record, ref branch, ref isDetached, ref hasCommits,
                            ref upstream, ref ahead, ref behind);
                continue;
            }

            switch (record[0])
            {
                case '1':
                    changes.Add(ParseOrdinary(record));
                    break;

                case '2':
                    // The original path is the very next NUL-terminated field.
                    var originalPath = i + 1 < records.Length ? records[++i] : null;
                    changes.Add(ParseRenameOrCopy(record, originalPath));
                    break;

                case 'u':
                    changes.Add(new FileChange(
                        PathAfterFields(record, 10), null, ChangeKind.Unmerged, ChangeKind.Unmerged));
                    break;

                case '?':
                    changes.Add(new FileChange(
                        record[2..], null, ChangeKind.None, ChangeKind.Untracked));
                    break;

                case '!':
                    // Ignored files are not shown.
                    break;
            }
        }

        return new StatusSnapshot(branch, isDetached, hasCommits, upstream, ahead, behind, changes);
    }

    private static void ParseHeader(
        string record, ref string? branch, ref bool isDetached, ref bool hasCommits,
        ref string? upstream, ref int ahead, ref int behind)
    {
        var parts = record.Split(' ', 3);
        if (parts.Length < 3) return;

        switch (parts[1])
        {
            case "branch.oid":
                hasCommits = parts[2] != "(initial)";
                break;

            case "branch.head":
                if (parts[2] == "(detached)")
                {
                    isDetached = true;
                    branch = null;
                }
                else
                {
                    branch = parts[2];
                }
                break;

            case "branch.upstream":
                upstream = parts[2];
                break;

            case "branch.ab":
                // Format: "+2 -3"
                foreach (var token in parts[2].Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (token.StartsWith('+') && int.TryParse(token[1..], out var a)) ahead = a;
                    else if (token.StartsWith('-') && int.TryParse(token[1..], out var b)) behind = b;
                }
                break;
        }
    }

    /// <summary>Ordinary record: 8 fixed fields, then the path.</summary>
    private static FileChange ParseOrdinary(string record)
    {
        var xy = record.Substring(2, 2);
        return new FileChange(
            PathAfterFields(record, 8),
            null,
            FromCode(xy[0]),
            FromCode(xy[1]));
    }

    /// <summary>Rename/copy record: 9 fixed fields (the extra one is the similarity score), then the path.</summary>
    private static FileChange ParseRenameOrCopy(string record, string? originalPath)
    {
        var xy = record.Substring(2, 2);
        return new FileChange(
            PathAfterFields(record, 9),
            originalPath,
            FromCode(xy[0]),
            FromCode(xy[1]));
    }

    /// <summary>
    /// Returns everything after the first <paramref name="fieldCount"/> space-separated fields.
    /// Splitting the whole record on space would corrupt any path containing a space.
    /// </summary>
    private static string PathAfterFields(string record, int fieldCount)
    {
        var index = 0;
        for (var field = 0; field < fieldCount; field++)
        {
            index = record.IndexOf(' ', index);
            if (index < 0) return string.Empty;
            index++;
        }
        return record[index..];
    }

    private static ChangeKind FromCode(char code) => code switch
    {
        '.' => ChangeKind.None,
        'A' => ChangeKind.Added,
        'M' => ChangeKind.Modified,
        'D' => ChangeKind.Deleted,
        'R' => ChangeKind.Renamed,
        'C' => ChangeKind.Copied,
        'U' => ChangeKind.Unmerged,
        'T' => ChangeKind.Modified, // type change; a beginner does not need the distinction
        _ => ChangeKind.None,
    };
}
