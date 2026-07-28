using GitHelper.Core.Errors;

namespace GitHelper.Core.Setup;

/// <summary>The result of running a setup operation.</summary>
public sealed record SetupOutcome(
    bool Success,
    string? Narration,
    TranslatedError? Error,
    IReadOnlyList<string> Blockers);
