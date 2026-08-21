using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitHelper.Core.Content;
using GitHelper.Core.Model;
using GitHelper.Core.Repo;

namespace GitHelper.App.ViewModels;

/// <summary>
/// What the viewmodel needs from the reader, and nothing more. An interface so the states
/// below can be tested without a repository on disk.
/// </summary>
public interface IDiffSource
{
    /// <param name="originalPath">
    /// The name the file had before it was renamed, or null. Carried all the way down to the
    /// pathspec: git only reports a rename when both names are in it.
    /// </param>
    Task<FileDiff> ReadAsync(
        string repoPath, string path, DiffSide side, string? originalPath, CancellationToken ct);
}

/// <summary>The real one.</summary>
public sealed class GitDiffSource(DiffReader reader) : IDiffSource
{
    public Task<FileDiff> ReadAsync(
        string repoPath, string path, DiffSide side, string? originalPath, CancellationToken ct)
        => reader.ReadAsync(repoPath, path, side, originalPath, ct);
}

/// <summary>
/// One file's diff, open. Transient: it is about a single file, and persisting it would mean
/// deciding what happens when that file stops existing.
/// </summary>
public sealed partial class DiffViewModel(IDiffSource source, ContentLibrary content)
    : ViewModelBase
{
    /// <summary>
    /// The authored document that tells a beginner how to read what is below it. Named as a
    /// constant rather than spelled inline, the way action ids are.
    /// </summary>
    private const string IntroDocumentId = "file-changes";

    private string? _repoPath;
    private DiffSide _side;
    private string? _renamedFrom;

    public ObservableCollection<DiffHunk> Hunks { get; } = new();

    /// <summary>
    /// The short explanation shown above the hunks, taken from the content library rather
    /// than written into the view. This is the whole point of the reading/ category: the
    /// definitions of diff, hunk and the staging area live in one place, get their tooltips
    /// for free, and cannot drift from what the glossary says.
    /// </summary>
    public IReadOnlyList<ContentBlock> IntroBlocks { get; } =
        content.Reading[IntroDocumentId].What;

    [ObservableProperty] private string _path = string.Empty;
    [ObservableProperty] private string _sideLabel = string.Empty;
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private bool _hasMessage;
    [ObservableProperty] private bool _hasHunks;

    /// <summary>Raised when the user asks to go back. The shell decides what that means.</summary>
    public Action? CloseRequested { get; set; }

    private RelayCommand? _closeCommand;

    /// <summary>One instance, not a new command per binding: a fresh command each get would
    /// leave the view bound to an object nothing else can reach.</summary>
    public IRelayCommand CloseCommand =>
        _closeCommand ??= new RelayCommand(() => CloseRequested?.Invoke());

    public async Task OpenAsync(
        string repoPath,
        string path,
        DiffSide side,
        string? renamedFrom,
        CancellationToken ct)
    {
        _repoPath = repoPath;
        _side = side;
        _renamedFrom = renamedFrom;
        Path = path;
        SideLabel = side switch
        {
            DiffSide.Staged => "Changes you have staged, ready to commit",
            DiffSide.Untracked => "Every line in this new file",
            _ => "Changes you have not staged yet",
        };

        Hunks.Clear();
        Message = string.Empty;
        HasMessage = false;
        HasHunks = false;

        await ReadAsync(ct);
    }

    public Task RefreshAsync(CancellationToken ct)
        => _repoPath is null ? Task.CompletedTask : ReadAsync(ct);

    private async Task ReadAsync(CancellationToken ct)
    {
        FileDiff diff;
        try
        {
            diff = await source.ReadAsync(_repoPath!, Path, _side, _renamedFrom, ct);
        }
        catch (GitReadException)
        {
            // Keep whatever is on screen. Blanking it would say the user's changes had gone,
            // which is the most alarming possible lie and the reason RefreshAsync already
            // refuses to publish an empty snapshot on a failed read.
            Message = "The changes in this file could not be read just now.";
            HasMessage = true;
            return;
        }

        Hunks.Clear();
        foreach (var hunk in diff.Hunks) Hunks.Add(hunk);
        HasHunks = Hunks.Count > 0;

        Message = DescribeState(diff);
        HasMessage = Message.Length > 0;
    }

    private string DescribeState(FileDiff diff)
    {
        if (diff.Kind == DiffKind.Binary)
            return "This is not a text file, so there are no lines to compare. "
                + "Git can tell it changed, but not how.";

        if (diff.Truncated)
            return $"This file is too long to show in full. Showing the first "
                + $"{GitHelper.Core.Parsing.DiffParser.MaxLines} lines — to see all of it, run "
                + $"git diff -- {Path} in a terminal.";

        if (diff.Kind != DiffKind.Empty) return string.Empty;

        // Empty covers a rename with no edits, which is not "nothing changed" at all. The
        // diff genuinely does not carry the old name; the snapshot does.
        if (_renamedFrom is not null)
            return $"This file was renamed from {_renamedFrom}. Its contents are unchanged.";

        return _side switch
        {
            DiffSide.Staged => "There are no staged changes in this file any more.",
            DiffSide.Untracked => "This file is no longer new to git.",
            _ => "There are no unstaged changes in this file any more.",
        };
    }
}
