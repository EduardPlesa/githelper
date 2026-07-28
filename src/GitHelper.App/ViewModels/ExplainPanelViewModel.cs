using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitHelper.App.Infrastructure;
using GitHelper.App.Settings;
using GitHelper.Core.Actions;
using GitHelper.Core.Content;
using GitHelper.Core.Errors;
using GitHelper.Core.Setup;

namespace GitHelper.App.ViewModels;

public enum ExplainPanelState
{
    /// <summary>Nothing selected — shows the quiet placeholder.</summary>
    Empty,

    /// <summary>Showing an action's explanation, possibly awaiting confirmation.</summary>
    Explaining,

    /// <summary>The action ran and git failed; showing the translated error.</summary>
    Error,
}

/// <summary>
/// Drives the right-hand panel: previews an action, gates it by danger level, runs it, and
/// reports what actually happened. Owns the "modal only for Destructive" rule.
/// </summary>
public sealed partial class ExplainPanelViewModel : ViewModelBase
{
    private static readonly IReadOnlyList<ContentBlock> NoBlocks = Array.Empty<ContentBlock>();

    private readonly ActionService _actions;
    private readonly IConfirmationDialog _confirmations;
    private readonly ISettingsStore _settings;
    private readonly SetupService? _setup;

    private string? _repoPath;
    private ActionRequest? _request;
    private IReadOnlyDictionary<string, string> _slots = new Dictionary<string, string>();

    private string? _folderPath;
    private SetupRequest? _setupRequest;

    public ExplainPanelViewModel(
        ActionService actions,
        IConfirmationDialog confirmations,
        ISettingsStore settings,
        SetupService? setup = null)
    {
        _actions = actions;
        _confirmations = confirmations;
        _settings = settings;
        _setup = setup;

        ConfirmCommand = new AsyncRelayCommand(
            () => _setupRequest is null ? RunAsync() : RunSetupAsync(), () => CanRun);
        ToggleTechnicalDetailsCommand = new RelayCommand(
            () => ShowTechnicalDetails = !ShowTechnicalDetails);
    }

    [ObservableProperty] private ExplainPanelState _panelState = ExplainPanelState.Empty;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _commandLine = string.Empty;
    [ObservableProperty] private string? _fileContents;
    [ObservableProperty] private Danger _dangerLevel;
    [ObservableProperty] private IReadOnlyList<ContentBlock> _whatBlocks = NoBlocks;
    [ObservableProperty] private IReadOnlyList<ContentBlock> _risksBlocks = NoBlocks;
    [ObservableProperty] private IReadOnlyList<ContentBlock> _undoBlocks = NoBlocks;
    [ObservableProperty] private IReadOnlyList<string> _blockers = Array.Empty<string>();
    [ObservableProperty] private bool _canRun;
    [ObservableProperty] private bool _requiresConfirmation;
    [ObservableProperty] private string? _narration;
    [ObservableProperty] private TranslatedError? _error;
    [ObservableProperty] private bool _showTechnicalDetails;
    [ObservableProperty] private bool _suppressExplanationForThisAction;

    /// <summary>
    /// Invoked after a run so the shell can refresh repository state, and awaited so the
    /// refresh completes before RunAsync returns. A plain event could not be awaited.
    /// </summary>
    public Func<ActionOutcome, CancellationToken, Task>? ActionCompletedAsync { get; set; }

    /// <summary>
    /// Invoked after a setup operation, and awaited. The shell uses it to open a folder that
    /// has just become a repository.
    /// </summary>
    public Func<SetupOutcome, CancellationToken, Task>? SetupCompletedAsync { get; set; }

    public IAsyncRelayCommand ConfirmCommand { get; }

    public IRelayCommand ToggleTechnicalDetailsCommand { get; }

    // Compiled bindings cannot compare an enum to a constant, so the conditions the view
    // needs are exposed as booleans rather than pushed into XAML converters.
    public bool IsEmpty => PanelState == ExplainPanelState.Empty;

    public bool HasBlockers => Blockers.Count > 0;

    public bool HasNarration => !string.IsNullOrEmpty(Narration);

    public bool HasError => Error is not null;

    /// <summary>
    /// True when this operation writes a file instead of running a command — the panel shows
    /// "The file" in place of "The command" rather than inventing a command that does not exist.
    /// </summary>
    public bool HasFileContents => !string.IsNullOrEmpty(FileContents);

    public bool HasCommandLine => !string.IsNullOrEmpty(CommandLine);

    /// <summary>
    /// True when the confirmation belongs inline in the panel. Destructive actions are
    /// excluded: they are gated by a modal instead, deliberately in a different screen
    /// position so the confirmation cannot be dismissed by the muscle memory built up
    /// clicking inline Confirm buttons.
    /// </summary>
    public bool RequiresInlineConfirmation =>
        RequiresConfirmation && DangerLevel != Danger.Destructive;

    /// <summary>
    /// True when clicking the action should proceed without waiting for an inline Confirm.
    /// Destructive actions qualify: RunAsync opens the modal itself, so the gate is not
    /// skipped — it simply lives in the dialog rather than in the panel.
    /// </summary>
    public bool ShouldRunImmediately => CanRun && !RequiresInlineConfirmation;

    /// <summary>Previews an action without running anything.</summary>
    public async Task ShowAsync(string repoPath, ActionRequest request, CancellationToken ct = default)
    {
        var preview = await _actions.PreviewAsync(repoPath, request, ct);

        // Arming the action path disarms the setup path, so the two can never both fire:
        // otherwise a stale _setupRequest from an earlier setup preview would make
        // ConfirmCommand run that setup instead of the action just shown to the user.
        _folderPath = null;
        _setupRequest = null;

        _repoPath = repoPath;
        _request = request;
        _slots = preview.Slots;

        Title = preview.Action.Title;
        CommandLine = preview.CommandLine;
        FileContents = null;
        DangerLevel = preview.Danger;
        // Fully qualified: this file already imports GitHelper.Core.Content, so a bare
        // "Content.SlotResolver" would be ambiguous between the two Content namespaces.
        WhatBlocks = GitHelper.App.Content.SlotResolver.Resolve(preview.Explanation.What, preview.Slots);
        RisksBlocks = GitHelper.App.Content.SlotResolver.Resolve(preview.Explanation.Risks, preview.Slots);
        UndoBlocks = GitHelper.App.Content.SlotResolver.Resolve(preview.Explanation.Undo, preview.Slots);
        Blockers = preview.Blockers
            .Select(b => b.Message ?? "This cannot run right now.")
            .ToArray();
        CanRun = preview.CanRun;
        RequiresConfirmation = ComputeRequiresConfirmation(preview.Danger, request.ActionId);

        Narration = null;
        Error = null;
        ShowTechnicalDetails = false;
        SuppressExplanationForThisAction = false;
        PanelState = ExplainPanelState.Explaining;
    }

    /// <summary>
    /// Runs the previewed action. Returns false when nothing ran — either because it was
    /// blocked, or because the user declined the destructive confirmation.
    /// </summary>
    public async Task<bool> RunAsync(CancellationToken ct = default)
    {
        if (_repoPath is null || _request is null || !CanRun) return false;

        if (DangerLevel == Danger.Destructive)
        {
            var confirmed = await _confirmations.ConfirmDestructiveAsync(
                Title, BuildConsequence(), ct);
            if (!confirmed) return false;
        }

        var outcome = await _actions.RunAsync(_repoPath, _request, ct);

        if (outcome.Success)
        {
            Narration = outcome.Narration;
            Error = null;
            PanelState = ExplainPanelState.Explaining;
        }
        else
        {
            Narration = null;
            Error = outcome.Error;
            ShowTechnicalDetails = false;
            PanelState = ExplainPanelState.Error;
        }

        if (SuppressExplanationForThisAction && DangerLevel != Danger.Destructive)
            _settings.Save(_settings.Load().WithExplanationSuppressed(_request.ActionId));

        if (ActionCompletedAsync is { } handler) await handler(outcome, ct);
        return true;
    }

    /// <summary>Shows the explanation, then runs the action if it needs no confirmation.</summary>
    public async Task ShowAndRunIfUngatedAsync(
        string repoPath, ActionRequest request, CancellationToken ct = default)
    {
        await ShowAsync(repoPath, request, ct);
        if (ShouldRunImmediately) await RunAsync(ct);
    }

    /// <summary>Previews a setup operation. Runs nothing.</summary>
    public async Task ShowSetupAsync(
        string folderPath, SetupRequest request, CancellationToken ct = default)
    {
        if (_setup is null)
            throw new InvalidOperationException("This panel was built without a SetupService.");

        var preview = await _setup.PreviewAsync(folderPath, request, ct);

        // Arming the setup path disarms the action path, so the two can never both fire.
        _repoPath = null;
        _request = null;
        _slots = new Dictionary<string, string>();

        _folderPath = folderPath;
        _setupRequest = request;

        Title = preview.Title;
        CommandLine = preview.CommandLine ?? string.Empty;
        FileContents = preview.FileContents;
        DangerLevel = Danger.Safe;
        WhatBlocks = preview.Explanation.What;
        RisksBlocks = preview.Explanation.Risks;
        UndoBlocks = preview.Explanation.Undo;
        Blockers = preview.Blockers.ToArray();
        CanRun = preview.CanRun;
        RequiresConfirmation = true;

        Narration = null;
        Error = null;
        ShowTechnicalDetails = false;
        SuppressExplanationForThisAction = false;
        PanelState = ExplainPanelState.Explaining;
    }

    /// <summary>Runs the previewed setup operation. False when nothing ran.</summary>
    public async Task<bool> RunSetupAsync(CancellationToken ct = default)
    {
        if (_setup is null || _folderPath is null || _setupRequest is null || !CanRun) return false;

        var outcome = await _setup.RunAsync(_folderPath, _setupRequest, ct);

        if (outcome.Success)
        {
            Narration = outcome.Narration;
            Error = null;
            PanelState = ExplainPanelState.Explaining;
        }
        else
        {
            Narration = null;
            Error = outcome.Error;
            Blockers = outcome.Blockers.ToArray();
            PanelState = outcome.Error is null
                ? ExplainPanelState.Explaining
                : ExplainPanelState.Error;
        }

        if (SetupCompletedAsync is { } handler) await handler(outcome, ct);
        return outcome.Success;
    }

    public void Clear()
    {
        _repoPath = null;
        _request = null;
        _slots = new Dictionary<string, string>();

        _folderPath = null;
        _setupRequest = null;

        Title = string.Empty;
        CommandLine = string.Empty;
        FileContents = null;
        WhatBlocks = NoBlocks;
        RisksBlocks = NoBlocks;
        UndoBlocks = NoBlocks;
        Blockers = Array.Empty<string>();
        CanRun = false;
        RequiresConfirmation = false;
        Narration = null;
        Error = null;
        ShowTechnicalDetails = false;
        SuppressExplanationForThisAction = false;
        PanelState = ExplainPanelState.Empty;
    }

    private bool ComputeRequiresConfirmation(Danger danger, string actionId) => danger switch
    {
        Danger.Safe => false,

        // Never suppressible: this is the one action that can permanently lose work.
        Danger.Destructive => true,

        _ => !_settings.Load().SuppressedExplanations.Contains(actionId),
    };

    /// <summary>The consequence sentence shown in the destructive modal, with real values.</summary>
    private string BuildConsequence()
    {
        var path = _slots.TryGetValue("path", out var p) ? p : "this file";
        return $"This permanently deletes your unsaved edits to {path}. "
               + "They were never committed, so nothing can bring them back.";
    }

    partial void OnCanRunChanged(bool value)
    {
        OnPropertyChanged(nameof(ShouldRunImmediately));
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    partial void OnPanelStateChanged(ExplainPanelState value)
        => OnPropertyChanged(nameof(IsEmpty));

    partial void OnBlockersChanged(IReadOnlyList<string> value)
        => OnPropertyChanged(nameof(HasBlockers));

    partial void OnNarrationChanged(string? value)
        => OnPropertyChanged(nameof(HasNarration));

    partial void OnErrorChanged(TranslatedError? value)
        => OnPropertyChanged(nameof(HasError));

    partial void OnFileContentsChanged(string? value)
        => OnPropertyChanged(nameof(HasFileContents));

    partial void OnCommandLineChanged(string value)
        => OnPropertyChanged(nameof(HasCommandLine));

    partial void OnRequiresConfirmationChanged(bool value)
    {
        OnPropertyChanged(nameof(RequiresInlineConfirmation));
        OnPropertyChanged(nameof(ShouldRunImmediately));
    }

    partial void OnDangerLevelChanged(Danger value)
    {
        OnPropertyChanged(nameof(RequiresInlineConfirmation));
        OnPropertyChanged(nameof(ShouldRunImmediately));
    }
}
