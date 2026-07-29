using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using GitHelper.App.ViewModels;
using GitHelper.App.Views;
using GitHelper.Core.Actions;
using GitHelper.Core.Content;
using GitHelper.Core.Git;
using GitHelper.Core.Repo;

namespace GitHelper.App.Tests;

public class TabViewTests
{
    private static ExplainPanelViewModel NewPanel()
    {
        var runner = new GitRunner();
        var service = new ActionService(runner, new RepoStateReader(runner), ContentLibrary.Load());
        return new ExplainPanelViewModel(service, new StubConfirmationDialog(), new InMemorySettingsStore());
    }

    private static async Task<(TestRepo Repo, RepoStateReader Reader)> RepoWithChangesAsync()
    {
        var repo = await TestRepo.CreateAsync();
        repo.WriteFile("staged.txt", "a\n");
        repo.WriteFile("untracked.txt", "b\n");
        await repo.GitAsync("add", "--", "staged.txt");
        return (repo, new RepoStateReader(new GitRunner()));
    }

    [AvaloniaFact]
    public void EveryTabViewRendersWithoutAViewModel()
    {
        // The shell builds views before a repository is open.
        foreach (var view in new Control[] { new ChangesView(), new HistoryView(), new BranchesView() })
        {
            var window = new Window { Content = view };
            window.Show();
            Assert.True(window.IsVisible);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ChangesView_ShowsStagedAndUnstagedRows()
    {
        var (repo, reader) = await RepoWithChangesAsync();
        using var _ = repo;
        var vm = new ChangesViewModel(NewPanel());
        vm.Update(await reader.ReadAsync(repo.Path), null);

        var view = new ChangesView { DataContext = vm };
        var window = new Window { Content = view };
        window.Show();

        Assert.NotNull(view.FindControl<ItemsControl>("StagedHost"));
        Assert.NotNull(view.FindControl<ItemsControl>("UnstagedHost"));
        Assert.Single(vm.Staged);
        Assert.Single(vm.Unstaged);
    }

    [AvaloniaFact]
    public async Task ChangesView_BindsTheCommitBoxBothWays()
    {
        var (repo, reader) = await RepoWithChangesAsync();
        using var _ = repo;
        var vm = new ChangesViewModel(NewPanel());
        vm.Update(await reader.ReadAsync(repo.Path), null);

        var view = new ChangesView { DataContext = vm };
        var window = new Window { Content = view };
        window.Show();

        var box = view.FindControl<TextBox>("CommitMessageBox");
        Assert.NotNull(box);

        box!.Text = "typed in the view";
        Assert.Equal("typed in the view", vm.CommitMessage);

        vm.CommitMessage = "set on the viewmodel";
        Assert.Equal("set on the viewmodel", box.Text);
    }

    [AvaloniaFact]
    public async Task HistoryView_ShowsCommitRows()
    {
        using var repo = await TestRepo.CreateAsync();
        var reader = new RepoStateReader(new GitRunner());
        var vm = new HistoryViewModel(NewPanel());
        vm.Update(await reader.ReadAsync(repo.Path));

        var view = new HistoryView { DataContext = vm };
        var window = new Window { Content = view };
        window.Show();

        Assert.NotNull(view.FindControl<ItemsControl>("CommitsHost"));
        Assert.Single(vm.Commits);
    }

    [AvaloniaFact]
    public async Task BranchesView_ShowsBranchRowsAndTheCurrentBranch()
    {
        using var repo = await TestRepo.CreateAsync();
        await repo.GitAsync("branch", "feature");
        var reader = new RepoStateReader(new GitRunner());
        var vm = new BranchesViewModel(NewPanel());
        vm.Update(await reader.ReadAsync(repo.Path));

        var view = new BranchesView { DataContext = vm };
        var window = new Window { Content = view };
        window.Show();

        Assert.NotNull(view.FindControl<ItemsControl>("BranchesHost"));
        Assert.NotNull(view.FindControl<TextBox>("NewBranchNameBox"));
        Assert.Equal(2, vm.Branches.Count);
        Assert.Equal("main", vm.CurrentBranchLabel);
    }

    [AvaloniaFact]
    public void ChangesView_ShowsTheConnectBoxWhenThereIsNoRemote()
    {
        var vm = new ChangesViewModel(NewPanel(), new StubBrowserLauncher());
        vm.Update(
            new GitHelper.Core.Model.RepoState(
                RepoRoot: @"C:\r", Branch: "main", IsDetached: false, Upstream: null,
                Ahead: 0, Behind: 0, HasCommits: true, HasRemote: false,
                Changes: Array.Empty<GitHelper.Core.Model.FileChange>(),
                RecentCommits: Array.Empty<GitHelper.Core.Model.CommitInfo>(),
                Branches: Array.Empty<GitHelper.Core.Model.BranchInfo>()),
            null);

        var view = new ChangesView { DataContext = vm };
        var window = new Window { Content = view };
        window.Show();

        var box = view.FindControl<TextBox>("RemoteUrlBox");
        Assert.NotNull(box);
        Assert.True(vm.HasNoRemoteOffer);

        box!.Text = "typed in the view";
        Assert.Equal("typed in the view", vm.RemoteUrl);

        vm.RemoteUrl = "set on the viewmodel";
        Assert.Equal("set on the viewmodel", box.Text);
    }

    [AvaloniaFact]
    public async Task BranchesView_BindsTheNewBranchNameBoxBothWays()
    {
        using var repo = await TestRepo.CreateAsync();
        var reader = new RepoStateReader(new GitRunner());
        var vm = new BranchesViewModel(NewPanel());
        vm.Update(await reader.ReadAsync(repo.Path));

        var view = new BranchesView { DataContext = vm };
        var window = new Window { Content = view };
        window.Show();

        var box = view.FindControl<TextBox>("NewBranchNameBox");
        Assert.NotNull(box);

        box!.Text = "feature";
        Assert.Equal("feature", vm.NewBranchName);
    }

    [AvaloniaFact]
    public async Task BranchesView_ShowsTheDisconnectButtonWhenThereIsARemote()
    {
        using var repo = await TestRepo.CreateAsync();
        await repo.GitAsync("remote", "add", "origin", "https://example.invalid/x.git");
        var reader = new RepoStateReader(new GitRunner());
        var vm = new BranchesViewModel(NewPanel());
        vm.Update(await reader.ReadAsync(repo.Path));

        var view = new BranchesView { DataContext = vm };
        var window = new Window { Content = view };
        window.Show();

        var button = view.FindControl<Button>("DisconnectRemoteButton");
        Assert.NotNull(button);
        Assert.True(vm.HasRemote);
        Assert.True(button!.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task BranchesView_HidesTheDisconnectButtonWhenThereIsNoRemote()
    {
        using var repo = await TestRepo.CreateAsync();
        var reader = new RepoStateReader(new GitRunner());
        var vm = new BranchesViewModel(NewPanel());
        vm.Update(await reader.ReadAsync(repo.Path));

        var view = new BranchesView { DataContext = vm };
        var window = new Window { Content = view };
        window.Show();

        var button = view.FindControl<Button>("DisconnectRemoteButton");
        Assert.NotNull(button);
        Assert.False(vm.HasRemote);
        Assert.False(button!.IsEffectivelyVisible);
    }
}
