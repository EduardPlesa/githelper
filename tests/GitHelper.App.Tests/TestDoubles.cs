using GitHelper.App.Infrastructure;
using GitHelper.App.Settings;
using GitHelper.Core.Git;

namespace GitHelper.App.Tests;

/// <summary>
/// Runs posted callbacks inline by default, so a viewmodel test sees the effect of a
/// background refresh without pumping a real dispatcher. Set RunInline to false to
/// assert that something was posted rather than executed.
///
/// Inline means "on the calling thread", and tests really do post from several threads at
/// once — a journey test has the file watcher refreshing on a thread-pool thread while an
/// action runs on the test thread, and both append to the same ObservableCollection. The
/// real dispatcher can never overlap two actions because it has one UI thread to run them
/// on, so the stub takes a gate to keep the same promise.
/// </summary>
public sealed class StubDispatcher : IUiDispatcher
{
    private readonly Lock _gate = new();
    private int _postCount;

    public bool IsOnUiThread => true;

    public bool RunInline { get; set; } = true;

    public int PostCount
    {
        get { lock (_gate) return _postCount; }
    }

    public void Post(Action action)
    {
        // The action runs under the gate, not merely the counter bump: serializing the
        // bookkeeping while letting the callbacks race would defeat the point.
        lock (_gate)
        {
            _postCount++;
            if (RunInline) action();
        }
    }
}

public sealed class StubFolderPicker : IFolderPicker
{
    public string? NextResult { get; set; }

    public int CallCount { get; private set; }

    public string? LastTitle { get; private set; }

    public Task<string?> PickFolderAsync(string title, CancellationToken ct = default)
    {
        CallCount++;
        LastTitle = title;
        return Task.FromResult(NextResult);
    }
}

public sealed class InMemorySettingsStore : ISettingsStore
{
    public AppSettings Current { get; set; } = AppSettings.Default;

    public int SaveCount { get; private set; }

    public AppSettings Load() => Current;

    public void Save(AppSettings settings)
    {
        Current = settings;
        SaveCount++;
    }
}

public sealed class StubConfirmationDialog : IConfirmationDialog
{
    public bool NextAnswer { get; set; }

    public int CallCount { get; private set; }

    public string? LastConsequence { get; private set; }

    public Task<bool> ConfirmDestructiveAsync(
        string title, string consequence, CancellationToken ct = default)
    {
        CallCount++;
        LastConsequence = consequence;
        return Task.FromResult(NextAnswer);
    }
}

/// <summary>
/// A canned git runner. ThrowGitMissing reproduces what the real runner does when git is
/// not on PATH: Process.Start throws Win32Exception, which GitEnvironment catches.
/// </summary>
public sealed class StubGitRunner : IGitRunner
{
    public Func<IReadOnlyList<string>, GitCommandResult>? Respond { get; set; }

    public bool ThrowGitMissing { get; set; }

    public Task<GitCommandResult> RunAsync(
        string workingDirectory, IReadOnlyList<string> args, CancellationToken ct = default)
    {
        if (ThrowGitMissing)
            throw new System.ComponentModel.Win32Exception("The system cannot find the file specified.");

        var result = Respond?.Invoke(args)
            ?? new GitCommandResult(args, string.Empty, string.Empty, 0, TimeSpan.Zero);

        return Task.FromResult(result);
    }
}

public sealed class StubBrowserLauncher : IBrowserLauncher
{
    public string? LastUrl { get; private set; }

    public int CallCount { get; private set; }

    public void Open(string url)
    {
        CallCount++;
        LastUrl = url;
    }
}
