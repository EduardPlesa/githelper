using GitHelper.App.Settings;
using GitHelper.App.ViewModels;
using GitHelper.Core.Actions;
using GitHelper.Core.Content;
using GitHelper.Core.Git;
using GitHelper.Core.Repo;

namespace GitHelper.App.Tests;

public class ExplainPanelViewModelTests
{
    private sealed record Fixture(
        ExplainPanelViewModel Panel,
        StubConfirmationDialog Confirmations,
        InMemorySettingsStore Settings);

    private static Fixture NewPanel()
    {
        var runner = new GitRunner();
        var service = new ActionService(runner, new RepoStateReader(runner), ContentLibrary.Load());
        var confirmations = new StubConfirmationDialog();
        var settings = new InMemorySettingsStore();
        return new Fixture(new ExplainPanelViewModel(service, confirmations, settings), confirmations, settings);
    }

    [Fact]
    public void StartsEmpty()
    {
        var (panel, _, _) = NewPanel();

        Assert.Equal(ExplainPanelState.Empty, panel.PanelState);
        Assert.Empty(panel.WhatBlocks);
    }

    [Fact]
    public async Task ShowAsync_PopulatesTheFourSectionsAndTheCommand()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("a.txt", "x\n");
        await repo.GitAsync("add", "-A");
        var (panel, _, _) = NewPanel();

        await panel.ShowAsync(repo.Path, new ActionRequest("commit", Message: "add a file"));

        Assert.Equal(ExplainPanelState.Explaining, panel.PanelState);
        Assert.Equal("Commit", panel.Title);
        Assert.Equal("git commit -m \"add a file\"", panel.CommandLine);
        Assert.NotEmpty(panel.WhatBlocks);
        Assert.NotEmpty(panel.RisksBlocks);
        Assert.NotEmpty(panel.UndoBlocks);
        Assert.True(panel.CanRun);
    }

    [Fact]
    public async Task ShowAsync_ResolvesSlotsSoNoSlotSpanSurvives()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("a.txt", "x\n");
        await repo.GitAsync("add", "-A");
        var (panel, _, _) = NewPanel();

        await panel.ShowAsync(repo.Path, new ActionRequest("commit", Message: "m"));

        Assert.All(
            panel.WhatBlocks.Concat(panel.RisksBlocks).Concat(panel.UndoBlocks),
            block => Assert.DoesNotContain(AllSpans(block), span => span is SlotSpan));

        static IEnumerable<InlineSpan> AllSpans(ContentBlock block) => block switch
        {
            ParagraphBlock p => p.Spans,
            BulletListBlock b => b.Items.SelectMany(i => i),
            _ => Array.Empty<InlineSpan>(),
        };
    }

    [Fact]
    public async Task ShowAsync_SurfacesBlockersAsPlainTextAndDisablesRunning()
    {
        using var repo = await TestRepo.CreateAsync();
        var (panel, _, _) = NewPanel();

        // Nothing is staged, so RequiresStagedChanges blocks the commit.
        await panel.ShowAsync(repo.Path, new ActionRequest("commit", Message: "m"));

        Assert.False(panel.CanRun);
        Assert.NotEmpty(panel.Blockers);
        Assert.Contains(panel.Blockers, b => b.Contains("staged", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SafeActionsNeedNoConfirmationAndRunImmediately()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("a.txt", "x\n");
        var (panel, _, _) = NewPanel();

        await panel.ShowAsync(repo.Path, new ActionRequest("stage-file", Path: "a.txt"));

        Assert.False(panel.RequiresConfirmation);
        Assert.True(panel.ShouldRunImmediately);
    }

    [Fact]
    public async Task CautionActionsRequireConfirmationUnlessSuppressed()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("a.txt", "x\n");
        await repo.GitAsync("add", "-A");
        var (panel, _, settings) = NewPanel();

        await panel.ShowAsync(repo.Path, new ActionRequest("commit", Message: "m"));
        Assert.True(panel.RequiresConfirmation);

        settings.Current = AppSettings.Default.WithExplanationSuppressed("commit");
        await panel.ShowAsync(repo.Path, new ActionRequest("commit", Message: "m"));

        Assert.False(panel.RequiresConfirmation);
    }

    [Fact]
    public async Task DestructiveConfirmationCanNeverBeSuppressed()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("README.md", "changed\n");
        var (panel, _, settings) = NewPanel();
        settings.Current = AppSettings.Default.WithExplanationSuppressed("discard-file");

        await panel.ShowAsync(repo.Path, new ActionRequest("discard-file", Path: "README.md"));

        Assert.True(panel.RequiresConfirmation);
        // ShouldRunImmediately is true here: it now tracks the *inline* gate only. The
        // suppression setting never reaches the modal, which RunAsync always consults for
        // a Destructive action regardless of this flag — that is the "never suppressed" part.
        Assert.True(panel.ShouldRunImmediately);
    }

    [Fact]
    public async Task RunAsync_ConsultsTheModalForTheDestructiveActionAndHonoursCancel()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("README.md", "vandalised\n");
        var (panel, confirmations, _) = NewPanel();
        confirmations.NextAnswer = false;

        await panel.ShowAsync(repo.Path, new ActionRequest("discard-file", Path: "README.md"));
        var ran = await panel.RunAsync();

        Assert.False(ran);
        Assert.Equal(1, confirmations.CallCount);
        Assert.Contains("README.md", confirmations.LastConsequence!);
        // Declining must leave the file untouched.
        Assert.Equal("vandalised\n", File.ReadAllText(Path.Combine(repo.Path, "README.md")).Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task RunAsync_ProceedsWhenTheModalIsAccepted()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("README.md", "vandalised\n");
        var (panel, confirmations, _) = NewPanel();
        confirmations.NextAnswer = true;

        await panel.ShowAsync(repo.Path, new ActionRequest("discard-file", Path: "README.md"));
        var ran = await panel.RunAsync();

        Assert.True(ran);
        Assert.Equal("hello\n", File.ReadAllText(Path.Combine(repo.Path, "README.md")).Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task RunAsync_NeverConsultsTheModalForANonDestructiveAction()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("a.txt", "x\n");
        await repo.GitAsync("add", "-A");
        var (panel, confirmations, _) = NewPanel();

        await panel.ShowAsync(repo.Path, new ActionRequest("commit", Message: "add a file"));
        await panel.RunAsync();

        Assert.Equal(0, confirmations.CallCount);
    }

    [Fact]
    public async Task RunAsync_ReportsNarrationOnSuccessAndRaisesActionCompleted()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("a.txt", "x\n");
        await repo.GitAsync("add", "-A");
        var (panel, _, _) = NewPanel();
        ActionOutcome? observed = null;
        panel.ActionCompletedAsync = (outcome, _) => { observed = outcome; return Task.CompletedTask; };

        await panel.ShowAsync(repo.Path, new ActionRequest("commit", Message: "add a file"));
        await panel.RunAsync();

        Assert.Contains("add a file", panel.Narration!);
        Assert.Null(panel.Error);
        Assert.NotNull(observed);
        Assert.True(observed!.Success);
    }

    [Fact]
    public async Task RunAsync_SwitchesToTheErrorStateAndKeepsRawOutputReachable()
    {
        using var repo = await TestRepo.CreateAsync();
        await repo.GitAsync("remote", "add", "origin", "https://example.invalid/nope.git");
        var (panel, _, _) = NewPanel();

        await panel.ShowAsync(repo.Path, new ActionRequest("push"));
        await panel.RunAsync();

        Assert.Equal(ExplainPanelState.Error, panel.PanelState);
        Assert.NotNull(panel.Error);
        Assert.NotEmpty(panel.Error!.RawOutput);
        Assert.False(panel.ShowTechnicalDetails); // collapsed until the user asks
    }

    [Fact]
    public async Task SuppressExplanationForThisAction_PersistsOnRun()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("a.txt", "x\n");
        await repo.GitAsync("add", "-A");
        var (panel, _, settings) = NewPanel();

        await panel.ShowAsync(repo.Path, new ActionRequest("commit", Message: "m"));
        panel.SuppressExplanationForThisAction = true;
        await panel.RunAsync();

        Assert.Contains("commit", settings.Current.SuppressedExplanations);
        Assert.Equal(1, settings.SaveCount);
    }

    [Fact]
    public async Task SuppressExplanationForThisAction_IsNeverPersistedForADestructiveAction()
    {
        // Critical safety rule: a user must never be able to silence the confirmation on
        // a destructive action. This guard must hold even when the action actually runs.
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("README.md", "modified\n");
        var (panel, confirmations, settings) = NewPanel();
        confirmations.NextAnswer = true;

        await panel.ShowAsync(repo.Path, new ActionRequest("discard-file", Path: "README.md"));
        panel.SuppressExplanationForThisAction = true;
        await panel.RunAsync();

        Assert.DoesNotContain("discard-file", settings.Current.SuppressedExplanations);
        Assert.Equal(0, settings.SaveCount);
    }

    [Fact]
    public async Task Clear_ReturnsToTheEmptyState()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("a.txt", "x\n");
        var (panel, _, _) = NewPanel();
        await panel.ShowAsync(repo.Path, new ActionRequest("stage-file", Path: "a.txt"));

        panel.Clear();

        Assert.Equal(ExplainPanelState.Empty, panel.PanelState);
        Assert.Empty(panel.WhatBlocks);
        Assert.Null(panel.Narration);
    }

    [Fact]
    public async Task ShowAndRunIfUngatedAsync_RunsSafeActionsWithoutASecondCall()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("a.txt", "x\n");
        var runner = new GitRunner();
        var reader = new RepoStateReader(runner);
        var (panel, _, _) = NewPanel();

        await panel.ShowAndRunIfUngatedAsync(repo.Path, new ActionRequest("stage-file", Path: "a.txt"));

        var state = await reader.ReadAsync(repo.Path);
        Assert.Single(state.Staged);
    }

    [Fact]
    public async Task ShowAndRunIfUngatedAsync_DoesNotRunAGatedAction()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("a.txt", "x\n");
        await repo.GitAsync("add", "-A");
        var runner = new GitRunner();
        var reader = new RepoStateReader(runner);
        var (panel, _, _) = NewPanel();

        await panel.ShowAndRunIfUngatedAsync(repo.Path, new ActionRequest("commit", Message: "m"));

        var state = await reader.ReadAsync(repo.Path);
        Assert.Single(state.RecentCommits); // still just the initial commit
    }

    [Fact]
    public async Task RequiresInlineConfirmation_IsFalseForADestructiveActionSoTheModalIsTheOnlyGate()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("README.md", "changed\n");
        var (panel, _, _) = NewPanel();

        await panel.ShowAsync(repo.Path, new ActionRequest("discard-file", Path: "README.md"));

        Assert.True(panel.RequiresConfirmation);
        Assert.False(panel.RequiresInlineConfirmation);
        Assert.True(panel.ShouldRunImmediately);
    }

    [Fact]
    public async Task RequiresInlineConfirmation_IsTrueForACautionAction()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("a.txt", "x\n");
        await repo.GitAsync("add", "-A");
        var (panel, _, _) = NewPanel();

        await panel.ShowAsync(repo.Path, new ActionRequest("commit", Message: "m"));

        Assert.True(panel.RequiresConfirmation);
        Assert.True(panel.RequiresInlineConfirmation);
        Assert.False(panel.ShouldRunImmediately);
    }

    [Fact]
    public async Task ShowAndRunIfUngatedAsync_ReachesTheModalForADestructiveAction()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("README.md", "vandalised\n");
        var (panel, confirmations, _) = NewPanel();
        confirmations.NextAnswer = false;

        await panel.ShowAndRunIfUngatedAsync(repo.Path, new ActionRequest("discard-file", Path: "README.md"));

        Assert.Equal(1, confirmations.CallCount);
        // Declining must leave the file untouched.
        Assert.Equal("vandalised\n", File.ReadAllText(Path.Combine(repo.Path, "README.md")).Replace("\r\n", "\n"));
    }
}
