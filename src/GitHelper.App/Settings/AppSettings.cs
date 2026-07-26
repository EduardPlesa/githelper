namespace GitHelper.App.Settings;

/// <summary>
/// Everything the app remembers between runs. Immutable: callers transform it with the
/// With… methods, so no caller can half-update it or bypass the recents cap.
/// </summary>
public sealed record AppSettings(
    IReadOnlyList<string> RecentRepositories,
    IReadOnlySet<string> SuppressedExplanations,
    AppTheme Theme)
{
    public const int MaxRecentRepositories = 8;

    public static AppSettings Default { get; } = new(
        Array.Empty<string>(),
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        AppTheme.System);

    /// <summary>Moves a repository to the front of the recents list, capped and de-duplicated.</summary>
    public AppSettings WithRepositoryOpened(string path)
    {
        var recents = new List<string> { path };
        recents.AddRange(RecentRepositories.Where(
            p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase)));

        return this with
        {
            RecentRepositories = recents.Take(MaxRecentRepositories).ToArray(),
        };
    }

    public AppSettings WithRepositoryRemoved(string path)
        => this with
        {
            RecentRepositories = RecentRepositories
                .Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase))
                .ToArray(),
        };

    public AppSettings WithExplanationSuppressed(string actionId)
    {
        var suppressed = new HashSet<string>(SuppressedExplanations, StringComparer.OrdinalIgnoreCase)
        {
            actionId,
        };

        return this with { SuppressedExplanations = suppressed };
    }

    public AppSettings WithTheme(AppTheme theme) => this with { Theme = theme };
}
