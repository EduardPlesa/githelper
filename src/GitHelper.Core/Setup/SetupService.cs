using GitHelper.Core.Content;
using GitHelper.Core.Errors;
using GitHelper.Core.Git;
using GitHelper.Core.Model;
using GitHelper.Core.Repo;

namespace GitHelper.Core.Setup;

/// <summary>
/// The preview-then-run flow for the two operations that happen before, or outside, ordinary
/// git actions. Mirrors ActionService's shape so the explain panel can drive both alike.
/// </summary>
public sealed class SetupService(
    IGitRunner runner,
    FolderInspector inspector,
    ContentLibrary content)
{
    public const string InitRepository = "init-repository";

    private static readonly string[] KnownOperations = { InitRepository };

    public Task<SetupPreview> PreviewAsync(
        string folderPath, SetupRequest request, CancellationToken ct = default)
    {
        RequireKnown(request.OperationId);

        var folder = inspector.Inspect(folderPath);
        var blockers = Evaluate(request.OperationId, folder);
        var document = content.Setup[request.OperationId];

        var args = InitArgs();
        var commandLine = new GitCommandResult(args, string.Empty, string.Empty, 0, TimeSpan.Zero)
            .CommandLine;

        return Task.FromResult(new SetupPreview(
            OperationId: request.OperationId,
            Title: document.Title,
            Explanation: document,
            CommandLine: commandLine,
            FileContents: null,
            Blockers: blockers));
    }

    /// <summary>
    /// Re-evaluates its blockers rather than trusting the preview: the caller is not trusted,
    /// and the folder may have changed since.
    /// </summary>
    public async Task<SetupOutcome> RunAsync(
        string folderPath, SetupRequest request, CancellationToken ct = default)
    {
        RequireKnown(request.OperationId);

        var folder = inspector.Inspect(folderPath);
        var blockers = Evaluate(request.OperationId, folder);
        if (blockers.Count > 0)
            return new SetupOutcome(false, null, null, blockers);

        var result = await runner.RunAsync(folderPath, InitArgs(), ct);

        return new SetupOutcome(
            Success: result.Success,
            Narration: result.Success
                ? "Started tracking this folder. Git is now watching it for changes."
                : null,
            Error: ErrorTranslator.Translate(result),
            Blockers: Array.Empty<string>());
    }

    private static string[] InitArgs() => new[] { "init", "-b", "main" };

    private static IReadOnlyList<string> Evaluate(string operationId, FolderState folder)
    {
        if (operationId == InitRepository && folder.IsRepository)
        {
            return new[]
            {
                "This folder is already a git project, so there is nothing to set up.",
            };
        }

        return Array.Empty<string>();
    }

    private static void RequireKnown(string operationId)
    {
        if (!KnownOperations.Contains(operationId))
            throw new ArgumentException($"Unknown setup operation '{operationId}'.", nameof(operationId));
    }
}
