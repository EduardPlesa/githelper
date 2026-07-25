namespace GitHelper.Core.Errors;

/// <summary>
/// A git failure, explained. <paramref name="RawOutput"/> is always populated, including
/// when the failure is understood — the UI shows it behind "show technical details" so a
/// user can always reach what git actually said.
/// </summary>
public sealed record TranslatedError(
    string Summary,
    string Explanation,
    IReadOnlyList<string> NextSteps,
    string RawOutput,
    bool IsUnderstood);
