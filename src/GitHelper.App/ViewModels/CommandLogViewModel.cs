using System.Collections.ObjectModel;
using GitHelper.App.Infrastructure;
using GitHelper.Core.Git;

namespace GitHelper.App.ViewModels;

/// <summary>
/// The always-visible strip of every git command this session. This is how a user
/// gradually learns the real CLI, so it is never hidden or filtered.
/// </summary>
public sealed class CommandLogViewModel : ViewModelBase, IDisposable
{
    private readonly CommandLog _log;
    private readonly IUiDispatcher _dispatcher;

    public CommandLogViewModel(CommandLog log, IUiDispatcher dispatcher)
    {
        _log = log;
        _dispatcher = dispatcher;

        // Commands may already have run before this strip was bound — the startup
        // environment check runs several.
        foreach (var entry in log.Entries) Entries.Add(entry);

        _log.EntryRecorded += OnEntryRecorded;
    }

    public ObservableCollection<CommandLogEntry> Entries { get; } = new();

    /// <summary>Just the commands, ready to paste into a terminal.</summary>
    public string ClipboardText => _log.ToClipboardText();

    public void Dispose() => _log.EntryRecorded -= OnEntryRecorded;

    private void OnEntryRecorded(object? sender, CommandLogEntry entry)
        // EntryRecorded fires on whichever thread ran git; the collection is bound to a
        // control, so the append has to reach the UI thread.
        => _dispatcher.Post(() => Entries.Add(entry));
}
