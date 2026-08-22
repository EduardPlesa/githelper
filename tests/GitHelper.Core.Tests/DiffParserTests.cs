using GitHelper.Core.Model;
using GitHelper.Core.Parsing;

namespace GitHelper.Core.Tests;

public class DiffParserTests
{
    private const string ModifiedFile =
        "diff --git a/a.txt b/a.txt\n" +
        "index 1234567..89abcde 100644\n" +
        "--- a/a.txt\n" +
        "+++ b/a.txt\n" +
        "@@ -1,3 +1,4 @@\n" +
        " one\n" +
        "-two\n" +
        "+TWO\n" +
        "+two and a half\n" +
        " three\n";

    [Fact]
    public void ReadsHunkCountsAndLineKinds()
    {
        var diff = DiffParser.Parse("a.txt", ModifiedFile);

        Assert.Equal(DiffKind.Text, diff.Kind);
        Assert.False(diff.Truncated);
        var hunk = Assert.Single(diff.Hunks);
        Assert.Equal("@@ -1,3 +1,4 @@", hunk.Header);
        Assert.Equal(1, hunk.OldStart);
        Assert.Equal(3, hunk.OldCount);
        Assert.Equal(1, hunk.NewStart);
        Assert.Equal(4, hunk.NewCount);

        Assert.Equal(
            new[]
            {
                DiffLineKind.Context, DiffLineKind.Removed, DiffLineKind.Added,
                DiffLineKind.Added, DiffLineKind.Context,
            },
            hunk.Lines.Select(l => l.Kind));
    }

    /// <summary>
    /// The numbers are what v3 will address lines by, and they are the part a reader cannot
    /// eyeball. A removed line has no line in the new file, and an added line none in the old.
    /// </summary>
    [Fact]
    public void NumbersEveryLineOnTheSideItExistsOn()
    {
        var lines = DiffParser.Parse("a.txt", ModifiedFile).Hunks.Single().Lines;

        Assert.Equal(new int?[] { 1, 2, null, null, 3 }, lines.Select(l => l.OldLineNumber));
        Assert.Equal(new int?[] { 1, null, 2, 3, 4 }, lines.Select(l => l.NewLineNumber));
    }

    /// <summary>Omitted counts mean 1. Requiring the comma silently drops single-line hunks.</summary>
    [Fact]
    public void AcceptsAHunkHeaderWithTheCountsOmitted()
    {
        var text =
            "diff --git a/a.txt b/a.txt\n" +
            "--- a/a.txt\n" +
            "+++ b/a.txt\n" +
            "@@ -1 +1 @@\n" +
            "-old\n" +
            "+new\n";

        var hunk = Assert.Single(DiffParser.Parse("a.txt", text).Hunks);

        Assert.Equal(1, hunk.OldCount);
        Assert.Equal(1, hunk.NewCount);
    }

    /// <summary>The shape a new file arrives in through --no-index. Zero old-start is legal.</summary>
    [Fact]
    public void AcceptsAZeroOldStartForAWhollyNewFile()
    {
        var text =
            "diff --git a/new.txt b/new.txt\n" +
            "new file mode 100644\n" +
            "index 0000000..fbbee86\n" +
            "--- /dev/null\n" +
            "+++ b/new.txt\n" +
            "@@ -0,0 +1,2 @@\n" +
            "+alpha\n" +
            "+beta\n";

        var hunk = Assert.Single(DiffParser.Parse("new.txt", text).Hunks);

        Assert.Equal(0, hunk.OldStart);
        Assert.Equal(0, hunk.OldCount);
        Assert.All(hunk.Lines, l => Assert.Equal(DiffLineKind.Added, l.Kind));
        Assert.Equal(new int?[] { 1, 2 }, hunk.Lines.Select(l => l.NewLineNumber));
    }

    /// <summary>
    /// The header's own --- and +++ lines would read as a removed and an added line under a
    /// colour-by-first-character rule. This is the single strongest reason a parser exists.
    /// </summary>
    [Fact]
    public void DoesNotMistakeFileHeadersForChangedLines()
    {
        var lines = DiffParser.Parse("a.txt", ModifiedFile).Hunks.Single().Lines;

        Assert.DoesNotContain(lines, l => l.Text is "-- a/a.txt" or "++ b/a.txt");
        Assert.Equal(5, lines.Count);
    }

    [Fact]
    public void ReadsTheNoNewlineMarkerAsItsOwnKind()
    {
        var text =
            "diff --git a/a.txt b/a.txt\n" +
            "--- a/a.txt\n" +
            "+++ b/a.txt\n" +
            "@@ -1 +1 @@\n" +
            "-old\n" +
            "+new\n" +
            "\\ No newline at end of file\n";

        var last = DiffParser.Parse("a.txt", text).Hunks.Single().Lines.Last();

        Assert.Equal(DiffLineKind.NoNewlineMarker, last.Kind);
        Assert.Null(last.OldLineNumber);
        Assert.Null(last.NewLineNumber);
    }

    [Theory]
    [InlineData("diff --git a/x.png b/x.png\nBinary files a/x.png and b/x.png differ\n")]
    [InlineData("diff --git a/x.png b/x.png\nGIT binary patch\nliteral 12\n")]
    public void ReportsBinaryFilesAsBinary(string text)
    {
        var diff = DiffParser.Parse("x.png", text);

        Assert.Equal(DiffKind.Binary, diff.Kind);
        Assert.Empty(diff.Hunks);
    }

    /// <summary>Legitimate: a file edited and reverted between the snapshot and the read.</summary>
    [Fact]
    public void ReportsEmptyOutputAsEmptyRatherThanFailing()
    {
        Assert.Equal(DiffKind.Empty, DiffParser.Parse("a.txt", string.Empty).Kind);
    }

    /// <summary>A staged rename with no edits: a header, and no hunks at all.</summary>
    [Fact]
    public void ReportsARenameWithNoContentChangeAsEmpty()
    {
        var text =
            "diff --git a/old.txt b/new.txt\n" +
            "similarity index 100%\n" +
            "rename from old.txt\n" +
            "rename to new.txt\n";

        Assert.Equal(DiffKind.Empty, DiffParser.Parse("new.txt", text).Kind);
    }

    [Fact]
    public void ReadsSeveralHunksFromOneFile()
    {
        var text =
            "diff --git a/a.txt b/a.txt\n" +
            "--- a/a.txt\n" +
            "+++ b/a.txt\n" +
            "@@ -1,2 +1,2 @@\n" +
            "-one\n" +
            "+ONE\n" +
            " two\n" +
            "@@ -10,2 +10,2 @@ some section heading\n" +
            "-ten\n" +
            "+TEN\n" +
            " eleven\n";

        var diff = DiffParser.Parse("a.txt", text);

        Assert.Equal(2, diff.Hunks.Count);
        Assert.Equal(10, diff.Hunks[1].OldStart);
        Assert.Equal("@@ -10,2 +10,2 @@ some section heading", diff.Hunks[1].Header);
    }

    [Fact]
    public void StopsAtTheLineCapAndSaysSo()
    {
        var body = string.Concat(Enumerable.Repeat("+line\n", DiffParser.MaxLines + 50));
        var text =
            "diff --git a/a.txt b/a.txt\n" +
            "--- a/a.txt\n" +
            "+++ b/a.txt\n" +
            $"@@ -0,0 +1,{DiffParser.MaxLines + 50} @@\n" +
            body;

        var diff = DiffParser.Parse("a.txt", text);

        Assert.True(diff.Truncated);
        Assert.Equal(DiffParser.MaxLines, diff.Hunks.Sum(h => h.Lines.Count));
    }

    /// <summary>
    /// Exactly at the cap is not over it. Git's output always ends with a newline, so
    /// Split('\n') hands the parser a trailing empty element; checking the cap before
    /// deciding whether a line is worth emitting made that empty element trip it, and the
    /// surface then offered a terminal command for a diff it had shown in full.
    /// </summary>
    [Fact]
    public void DoesNotCallADiffOfExactlyTheLineCapTruncated()
    {
        var body = string.Concat(Enumerable.Repeat("+line\n", DiffParser.MaxLines));
        var text =
            "diff --git a/a.txt b/a.txt\n" +
            "--- a/a.txt\n" +
            "+++ b/a.txt\n" +
            $"@@ -0,0 +1,{DiffParser.MaxLines} @@\n" +
            body;

        var diff = DiffParser.Parse("a.txt", text);

        Assert.False(diff.Truncated);
        Assert.Equal(DiffParser.MaxLines, diff.Hunks.Sum(h => h.Lines.Count));
    }

    /// <summary>
    /// Parse is public and documented as reading text nobody controls. A lone backslash is
    /// not something DiffReader can produce, but it must not take the app down either.
    /// </summary>
    [Fact]
    public void SurvivesANoNewlineMarkerWithNothingAfterIt()
    {
        var text =
            "diff --git a/a.txt b/a.txt\n" +
            "--- a/a.txt\n" +
            "+++ b/a.txt\n" +
            "@@ -1 +1 @@\n" +
            "-old\n" +
            "+new\n" +
            "\\\n";

        var last = DiffParser.Parse("a.txt", text).Hunks.Single().Lines.Last();

        Assert.Equal(DiffLineKind.NoNewlineMarker, last.Kind);
        Assert.Equal(string.Empty, last.Text);
    }

    /// <summary>
    /// Text is the line's own content, with no marker in it — that is what v3's guided
    /// conflict resolution will address and write back. The marker git prints for the kind
    /// is carried separately, because the app's glossary promises the user a plus and a
    /// minus and the view has to be able to show them.
    /// </summary>
    [Fact]
    public void KeepsTheLineContentCleanAndCarriesGitsMarkerBesideIt()
    {
        var lines = DiffParser.Parse("a.txt", ModifiedFile).Hunks.Single().Lines;

        Assert.Equal(
            new[] { "one", "two", "TWO", "two and a half", "three" },
            lines.Select(l => l.Text));

        Assert.Equal(new[] { " ", "-", "+", "+", " " }, lines.Select(l => l.Marker));
    }

    [Fact]
    public void MarksTheNoNewlineLineWithGitsOwnBackslash()
    {
        var text =
            "diff --git a/a.txt b/a.txt\n" +
            "--- a/a.txt\n" +
            "+++ b/a.txt\n" +
            "@@ -1 +1 @@\n" +
            "-old\n" +
            "+new\n" +
            "\\ No newline at end of file\n";

        var last = DiffParser.Parse("a.txt", text).Hunks.Single().Lines.Last();

        Assert.Equal("\\", last.Marker);
        Assert.Equal("No newline at end of file", last.Text);
    }
}
