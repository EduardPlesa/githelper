namespace GitHelper.Core.Model;

/// <summary>
/// The kinds of operation git can start and not finish. Only merge exists today; rebase,
/// cherry-pick and revert join it as each is built.
/// </summary>
public enum OperationKind
{
    Merge,
}

/// <summary>
/// An operation git has started and left in flight. Null on <see cref="RepoState"/> means
/// nothing is paused.
///
/// This is read from the repository on every refresh and never cached, so it survives the
/// app being closed and reopened — which it must, because the repository state does.
/// </summary>
/// <param name="IncomingLabel">
/// The name of what is being merged in, for the copy to use. Null when the merged commit
/// has no reachable name, in which case the UI says "another branch" instead.
/// </param>
public sealed record OperationState(OperationKind Kind, string? IncomingLabel);
