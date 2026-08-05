namespace GitHelper.Core.Model;

/// <summary>
/// A tag: a fixed name pointing at one commit. <paramref name="Target"/> is the short hash
/// it points to, for display only.
/// </summary>
public sealed record TagInfo(string Name, string Target);
