using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using GitHelper.App.ViewModels;
using GitHelper.App.Views;
using GitHelper.Core.Model;
using GitHelper.Core.Repo;

namespace GitHelper.App.Tests;

/// <summary>
/// The surface, rendered. These assertions belong here rather than in DiffViewModelTests
/// because the defect they guard against was a view that bound only Text: every kind of line
/// then reached the screen as identical monospace text, and the feature said nothing at all.
/// </summary>
public class DiffViewTests
{
    private sealed class StubSource(FileDiff diff) : IDiffSource
    {
        public Task<FileDiff> ReadAsync(
            string repoPath, string path, DiffSide side, string? originalPath, CancellationToken ct)
            => Task.FromResult(diff);
    }

    /// <summary>
    /// The code-behind renders the intro on DataContextChanged, and a view can be built
    /// before a viewmodel is attached to it. Nothing there may throw on a null DataContext.
    /// </summary>
    [AvaloniaFact]
    public void RendersWithNoViewModelAtAll()
    {
        var window = new Window { Content = new DiffView() };
        window.Show();

        Assert.True(window.IsVisible);
        window.Close();
    }

    /// <summary>One line of each kind that carries content, in a single hunk.</summary>
    private static FileDiff OneOfEachKind() => new(
        "a.txt",
        DiffKind.Text,
        new[]
        {
            new DiffHunk("@@ -1,2 +1,2 @@", 1, 2, 1, 2, new[]
            {
                new DiffLine(DiffLineKind.Context, "kept", 1, 1),
                new DiffLine(DiffLineKind.Removed, "gone", 2, null),
                new DiffLine(DiffLineKind.Added, "fresh", null, 2),
            }),
        },
        Truncated: false);

    private static async Task<DiffView> ShowAsync(FileDiff diff)
    {
        var viewModel = new DiffViewModel(new StubSource(diff), TestContent.Library);
        await viewModel.OpenAsync("repo", diff.Path, DiffSide.Unstaged, renamedFrom: null, default);

        var view = new DiffView { DataContext = viewModel };
        var window = new Window { Content = view };
        window.Show();
        view.UpdateLayout();

        return view;
    }

    private static List<Grid> LineRowsOf(DiffView view)
        => view.GetVisualDescendants().OfType<Grid>().Where(g => g.Name == "LineRow").ToList();

    private static List<string> MarkersOf(DiffView view)
        => view.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(t => t.Name == "LineMarker")
            .Select(t => t.Text ?? string.Empty)
            .ToList();

    /// <summary>
    /// The glossary this app ships says added lines "are marked with a plus, lines that were
    /// taken away with a minus". If the marker is not on screen, the shipped content is
    /// describing a diff the user is not looking at.
    /// </summary>
    [AvaloniaFact]
    public async Task ShowsGitsPlusAndMinusBesideEveryLine()
    {
        var view = await ShowAsync(OneOfEachKind());

        Assert.Equal(new[] { " ", "-", "+" }, MarkersOf(view));
    }

    /// <summary>
    /// Colour is the second signal, not the only one. An added and a removed line must not
    /// share a background, and neither may look like untouched context.
    /// </summary>
    [AvaloniaFact]
    public async Task TintsAddedAndRemovedLinesDifferentlyFromContextAndFromEachOther()
    {
        var view = await ShowAsync(OneOfEachKind());
        var rows = LineRowsOf(view);

        Assert.Equal(3, rows.Count);

        var colours = rows.Select(r => ((ISolidColorBrush)r.Background!).Color).ToList();

        Assert.Equal(Colors.Transparent, colours[0]);
        Assert.NotEqual(Colors.Transparent, colours[1]);
        Assert.NotEqual(Colors.Transparent, colours[2]);
        Assert.NotEqual(colours[1], colours[2]);
    }

    /// <summary>
    /// The line the parser produces is the line the file holds. Anything else and the text
    /// on screen disagrees with the text on disk by one character.
    /// </summary>
    [AvaloniaFact]
    public async Task ShowsTheLineContentWithoutTheMarkerEmbeddedInIt()
    {
        var view = await ShowAsync(OneOfEachKind());

        var texts = view.GetVisualDescendants()
            .OfType<TextBlock>()
            .Select(t => t.Text)
            .ToList();

        Assert.Contains("fresh", texts);
        Assert.Contains("gone", texts);
        Assert.DoesNotContain("+fresh", texts);
        Assert.DoesNotContain("-gone", texts);
    }

    /// <summary>
    /// The reading/ category existed but nothing in the app rendered it, so the one document
    /// in it never reached a user and the every-term-is-referenced rule was being satisfied
    /// by a document nobody could read. This is the test that makes that rule mean something.
    /// </summary>
    [AvaloniaFact]
    public async Task ShowsTheAuthoredIntroAboveTheHunks()
    {
        var view = await ShowAsync(OneOfEachKind());

        var host = view.FindControl<StackPanel>("IntroHost");
        Assert.NotNull(host);
        Assert.NotEmpty(host!.Children);
    }

    /// <summary>
    /// Underlined-with-a-tooltip is how this app treats jargon everywhere else, and the
    /// README promises it here by name for diff, hunk and the staging area. Rendering the
    /// authored document is what buys all three at once.
    /// </summary>
    [AvaloniaFact]
    public async Task UnderlinesTheJargonInTheIntroAndGivesItItsDefinition()
    {
        var view = await ShowAsync(OneOfEachKind());
        var host = view.FindControl<StackPanel>("IntroHost")!;

        var terms = host.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(t => t.TextDecorations == TextDecorations.Underline)
            .ToList();

        Assert.Equal(
            new[] { "diff", "hunk", "staging area" },
            terms.Select(t => t.Text ?? string.Empty).OrderBy(text => text, StringComparer.Ordinal));

        Assert.All(terms, t => Assert.NotNull(ToolTip.GetTip(t)));
    }

    /// <summary>
    /// The '@@' line is raw git output, not a glossary term. It used to be underlined and to
    /// carry a hand-written tooltip that restated terms/hunk.md — the copy-in-two-places
    /// failure the content library exists to prevent.
    /// </summary>
    [AvaloniaFact]
    public async Task LeavesTheHunkHeaderAsPlainGitOutputWithNoRestatedDefinition()
    {
        var view = await ShowAsync(OneOfEachKind());

        var header = view.GetVisualDescendants()
            .OfType<TextBlock>()
            .Single(t => t.Text == "@@ -1,2 +1,2 @@");

        Assert.Null(ToolTip.GetTip(header));
        Assert.NotEqual(TextDecorations.Underline, header.TextDecorations);
    }
}
