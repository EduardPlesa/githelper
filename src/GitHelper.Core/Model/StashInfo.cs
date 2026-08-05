namespace GitHelper.Core.Model;

/// <summary>
/// One stash entry. <paramref name="Ref"/> is git's own selector (e.g. "stash@{0}") and is
/// what every stash action passes straight back to git — it is never re-derived from the
/// entry's position in the list. Because git assigns it by position, though, it can end up
/// naming a different entry if the stash list changes between a refresh and a click; call
/// sites re-check it against live state rather than trusting a snapshot.
/// </summary>
public sealed record StashInfo(string Ref, string Message);
