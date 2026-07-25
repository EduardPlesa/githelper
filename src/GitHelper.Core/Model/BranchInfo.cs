namespace GitHelper.Core.Model;

/// <summary>A local branch. <paramref name="Upstream"/> is null when none is configured.</summary>
public sealed record BranchInfo(string Name, string? Upstream);
