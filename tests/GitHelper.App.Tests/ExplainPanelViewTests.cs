using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using GitHelper.App.ViewModels;
using GitHelper.App.Views;
using GitHelper.Core.Actions;
using GitHelper.Core.Content;
using GitHelper.Core.Git;
using GitHelper.Core.Repo;

namespace GitHelper.App.Tests;

public class ExplainPanelViewModelFlagTests
{
    private static ExplainPanelViewModel NewPanel()
    {
        var runner = new GitRunner();
        var service = new ActionService(runner, new RepoStateReader(runner), ContentLibrary.Load());
        return new ExplainPanelViewModel(service, new StubConfirmationDialog(), new InMemorySettingsStore());
    }

    [Fact]
    public void IsEmpty_IsTrueOnlyInTheEmptyState()
    {
        var panel = NewPanel();

        Assert.True(panel.IsEmpty);
        Assert.False(panel.HasError);
        Assert.False(panel.HasNarration);
        Assert.False(panel.HasBlockers);
    }

    [Fact]
    public async Task Flags_TrackTheUnderlyingStateAfterAPreview()
    {
        using var repo = await TestRepo.CreateAsync();
        var panel = NewPanel();

        // Nothing staged, so the commit is blocked.
        await panel.ShowAsync(repo.Path, new ActionRequest("commit", Message: "m"));

        Assert.False(panel.IsEmpty);
        Assert.True(panel.HasBlockers);
        Assert.False(panel.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public async Task ConfirmCommand_BecomesExecutableWhenTheActionCanRun()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("a.txt", "x\n");
        await repo.GitAsync("add", "-A");
        var panel = NewPanel();

        await panel.ShowAsync(repo.Path, new ActionRequest("commit", Message: "m"));

        Assert.True(panel.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public void ToggleTechnicalDetailsCommand_FlipsTheFlag()
    {
        var panel = NewPanel();

        panel.ToggleTechnicalDetailsCommand.Execute(null);
        Assert.True(panel.ShowTechnicalDetails);

        panel.ToggleTechnicalDetailsCommand.Execute(null);
        Assert.False(panel.ShowTechnicalDetails);
    }
}

public class ExplainPanelViewTests
{
    [AvaloniaFact]
    public void ExplainPanelView_RendersWithoutAViewModel()
    {
        // The shell creates views before a repository is open, so a null DataContext must
        // not throw.
        var window = new Window { Content = new ExplainPanelView() };

        window.Show();

        Assert.True(window.IsVisible);
    }

    [AvaloniaFact]
    public async Task ExplainPanelView_RendersAPreviewedActionsContent()
    {
        using var repo = await TestRepo.CreateAsync();
        repo.WriteFile("a.txt", "x\n");
        var runner = new GitRunner();
        var service = new ActionService(runner, new RepoStateReader(runner), ContentLibrary.Load());
        var panel = new ExplainPanelViewModel(
            service, new StubConfirmationDialog(), new InMemorySettingsStore());
        await panel.ShowAsync(repo.Path, new ActionRequest("stage-file", Path: "a.txt"));

        var view = new ExplainPanelView { DataContext = panel };
        var window = new Window { Content = view };
        window.Show();

        // The three block hosts should have been populated from the viewmodel's blocks.
        var whatHost = view.FindControl<StackPanel>("WhatHost");
        Assert.NotNull(whatHost);
        Assert.NotEmpty(whatHost!.Children);
    }

    [AvaloniaFact]
    public void CommandLogView_RendersWithoutAViewModel()
    {
        var window = new Window { Content = new CommandLogView() };

        window.Show();

        Assert.True(window.IsVisible);
    }

    [AvaloniaFact]
    public void CommandLogView_ShowsRecordedCommands()
    {
        var log = new CommandLog();
        log.Record(new GitCommandResult(new[] { "status" }, "", "", 0, TimeSpan.Zero));
        using var vm = new CommandLogViewModel(log, new StubDispatcher());

        var view = new CommandLogView { DataContext = vm };
        var window = new Window { Content = view };
        window.Show();

        var list = view.FindControl<ItemsControl>("EntriesHost");
        Assert.NotNull(list);
        Assert.Single(vm.Entries);
    }

    [AvaloniaFact]
    public void DiscardConfirmationDialog_CanBeConstructedWithItsCopy()
    {
        var dialog = new DiscardConfirmationDialog();

        dialog.SetContent("Discard changes to file", "This permanently deletes your edits.");

        Assert.Contains("permanently", dialog.ConsequenceText);
    }
}
