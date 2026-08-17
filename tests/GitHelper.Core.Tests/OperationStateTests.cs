using GitHelper.Core.Model;

namespace GitHelper.Core.Tests;

public class OperationStateTests
{
    [Fact]
    public void AMergeCarriesNoRebaseProgress()
    {
        // Merge has no steps. Carrying zeros would invite code that reads them.
        var merge = new OperationState(OperationKind.Merge, "feature");

        Assert.Null(merge.Rebase);
    }

    [Fact]
    public void ARebaseCarriesItsPlaceInTheSequence()
    {
        var rebase = new OperationState(
            OperationKind.Rebase, "main", new RebaseProgress(3, 7, "fix login bug"));

        Assert.Equal(3, rebase.Rebase!.Step);
        Assert.Equal(7, rebase.Rebase.Total);
        Assert.Equal("fix login bug", rebase.Rebase.StoppedAtSubject);
    }

    [Fact]
    public void ARebaseMayHaveNoProgressWhenGitDoesNotSayWhere()
    {
        // msgnum and end are not documented API. When they cannot be read the rebase is
        // still known to be running; only the counter is missing.
        var rebase = new OperationState(OperationKind.Rebase, "main");

        Assert.Null(rebase.Rebase);
    }
}
