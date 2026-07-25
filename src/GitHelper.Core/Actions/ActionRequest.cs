namespace GitHelper.Core.Actions;

/// <summary>
/// Names an action and its parameters. The UI never builds a git command; it names an
/// action and supplies these values.
/// </summary>
public sealed record ActionRequest(
    string ActionId,
    string? Path = null,
    string? Message = null,
    string? BranchName = null);
