using GitHelper.App.Infrastructure;
using GitHelper.App.Settings;

namespace GitHelper.App.Tests;

/// <summary>
/// Runs posted callbacks inline by default, so a viewmodel test sees the effect of a
/// background refresh without pumping a real dispatcher. Set RunInline to false to
/// assert that something was posted rather than executed.
/// </summary>
public sealed class StubDispatcher : IUiDispatcher
{
    public bool IsOnUiThread => true;

    public bool RunInline { get; set; } = true;

    public int PostCount { get; private set; }

    public void Post(Action action)
    {
        PostCount++;
        if (RunInline) action();
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
