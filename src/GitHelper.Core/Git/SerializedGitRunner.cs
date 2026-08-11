using System.Collections.Concurrent;

namespace GitHelper.Core.Git;

/// <summary>
/// Runs one git command at a time per repository, then delegates.
///
/// git does not support two index-writing commands in one repository at once: the second
/// dies with "Unable to create '.git/index.lock': File exists", exit 128. That is easy to
/// hit here without meaning to — staging writes <c>.git/index</c>, the file watcher wakes
/// and refreshes, and `git status` takes index.lock to write back its refreshed stat cache.
/// A commit started inside that window fails outright, and the user sees an untranslated
/// error for something they did nothing wrong to cause.
///
/// The refresh path already serialized refreshes against each other, but nothing stopped an
/// action from running alongside one. Putting the gate here rather than in the viewmodel
/// covers every caller by construction, which is the same reason argv lives in one place.
///
/// A decorator rather than logic inside <see cref="GitRunner"/>: that class has one job,
/// starting a process correctly, and is the part of the system least worth disturbing.
/// </summary>
public sealed class SerializedGitRunner(IGitRunner inner) : IGitRunner
{
    /// <summary>
    /// One gate per repository. Two projects open at once have no reason to queue behind
    /// each other, and git has no problem with it.
    ///
    /// Paths are compared case-insensitively because the same repository arrives spelled
    /// differently depending on who reports it — the OS via the file watcher, or git via
    /// rev-parse. Two gates for one repository would defeat the whole purpose.
    /// </summary>
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task<GitCommandResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> args,
        CancellationToken ct = default)
    {
        var gate = _gates.GetOrAdd(workingDirectory, _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(ct);

        try
        {
            return await inner.RunAsync(workingDirectory, args, ct);
        }
        finally
        {
            gate.Release();
        }
    }
}
