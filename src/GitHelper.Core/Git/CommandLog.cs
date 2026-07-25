namespace GitHelper.Core.Git;

/// <summary>
/// Every git command run this session. This is the mechanism by which a user outgrows the
/// app: the CLI is absorbed by watching it accumulate.
///
/// Thread-safe, because state refreshes run off the UI thread while actions may also be running.
/// </summary>
public sealed class CommandLog
{
    private readonly List<CommandLogEntry> _entries = new();
    private readonly Lock _gate = new();

    public event EventHandler<CommandLogEntry>? EntryRecorded;

    public IReadOnlyList<CommandLogEntry> Entries
    {
        get
        {
            lock (_gate) return _entries.ToArray();
        }
    }

    public void Record(GitCommandResult result)
    {
        var entry = new CommandLogEntry(
            At: DateTimeOffset.Now,
            CommandLine: result.CommandLine,
            ExitCode: result.ExitCode,
            Duration: result.Duration,
            Success: result.Success);

        lock (_gate) _entries.Add(entry);

        // Raised outside the lock so a handler cannot deadlock the runner.
        EntryRecorded?.Invoke(this, entry);
    }

    public void Clear()
    {
        lock (_gate) _entries.Clear();
    }

    /// <summary>The commands alone, ready to paste into a terminal.</summary>
    public string ToClipboardText()
        => string.Join(Environment.NewLine, Entries.Select(e => e.CommandLine));
}
