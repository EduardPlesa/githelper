namespace GitHelper.Core.Model;

/// <summary>
/// One stash entry. <paramref name="Ref"/> is git's own selector (e.g. "stash@{0}") and is
/// what every stash action passes straight back to git — it is never re-derived from the
/// entry's position in the list.
/// </summary>
public sealed record StashInfo(string Ref, string Message);
