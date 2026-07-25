namespace GitHelper.Core.Git;

public enum CheckStatus
{
    Ok,

    /// <summary>Something will go wrong later, but the app is still usable now.</summary>
    Warning,

    /// <summary>The app cannot function at all.</summary>
    Blocking,
}

/// <summary>One startup check, phrased for someone who does not know git.</summary>
public sealed record EnvironmentCheck(
    string Id,
    CheckStatus Status,
    string Summary,
    string Explanation,
    string? FixHint);
