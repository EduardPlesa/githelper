using System.Globalization;
using GitHelper.Core.Model;

namespace GitHelper.Core.Parsing;

/// <summary>Parses the delimited commit format produced by <see cref="Format"/>.</summary>
public static class LogParser
{
    private const char UnitSeparator = '';
    private const char RecordSeparator = '';

    /// <summary>
    /// Field and record separators are ASCII control characters, which cannot appear in
    /// commit metadata. A tab or newline delimiter would be corrupted by commit subjects.
    /// </summary>
    public const string Format = "%H%x1f%h%x1f%an%x1f%aI%x1f%s%x1e";

    public static IReadOnlyList<CommitInfo> Parse(string output)
    {
        var commits = new List<CommitInfo>();

        foreach (var record in output.Split(RecordSeparator))
        {
            // git separates records with newlines in addition to our separator.
            var trimmed = record.Trim('\n', '\r');
            if (trimmed.Length == 0) continue;

            var fields = trimmed.Split(UnitSeparator);
            if (fields.Length < 5) continue;

            var date = DateTimeOffset.TryParse(
                fields[3], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? parsed
                : DateTimeOffset.MinValue;

            commits.Add(new CommitInfo(fields[0], fields[1], fields[2], date, fields[4]));
        }

        return commits;
    }
}
