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
    public const string CreateGitignore = "create-gitignore";

    /// <summary>
    /// One entry per operation, pairing how it previews with how it runs. This is the single
    /// source of truth for "known operations": both PreviewAsync and RunAsync dispatch through
    /// this same table, and a <see cref="OperationHandlers"/> requires both delegates to
    /// construct. That makes it impossible to register an operation's preview without also
    /// supplying how it runs (or vice versa) - unlike two independent
    /// `if (operationId == ...)` chains in PreviewAsync and RunAsync, where editing one and
    /// forgetting the other compiles fine and silently runs the wrong operation.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, OperationHandlers> Operations =
        new Dictionary<string, OperationHandlers>
        {
            [InitRepository] = new(
                Preview: _ => new PreviewContent(
                    CommandLine: new GitCommandResult(
                        InitArgs(), string.Empty, string.Empty, 0, TimeSpan.Zero).CommandLine,
                    FileContents: null),
                Run: async (gitRunner, folderPath, _, ct) =>
                {
                    var result = await gitRunner.RunAsync(folderPath, InitArgs(), ct);

                    return new SetupOutcome(
                        Success: result.Success,
                        Narration: result.Success
                            ? "Started tracking this folder. Git is now watching it for changes."
                            : null,
                        Error: ErrorTranslator.Translate(result),
                        Blockers: Array.Empty<string>());
                }),

            [CreateGitignore] = new(
                Preview: folder => new PreviewContent(
                    CommandLine: null,
                    FileContents: GitignoreTemplates.For(folder.ProjectType)),
                Run: (_, _, folder, _) => Task.FromResult(WriteGitignore(folder))),
        };

    public Task<SetupPreview> PreviewAsync(
        string folderPath, SetupRequest request, CancellationToken ct = default)
    {
        var handlers = Require(request.OperationId);

        var folder = inspector.Inspect(folderPath);
        var blockers = Evaluate(request.OperationId, folder);
        var document = content.Setup[request.OperationId];
        var preview = handlers.Preview(folder);

        return Task.FromResult(new SetupPreview(
            OperationId: request.OperationId,
            Title: document.Title,
            Explanation: document,
            CommandLine: preview.CommandLine,
            FileContents: preview.FileContents,
            Blockers: blockers));
    }

    /// <summary>
    /// Re-evaluates its blockers rather than trusting the preview: the caller is not trusted,
    /// and the folder may have changed since.
    /// </summary>
    public async Task<SetupOutcome> RunAsync(
        string folderPath, SetupRequest request, CancellationToken ct = default)
    {
        var handlers = Require(request.OperationId);

        var folder = inspector.Inspect(folderPath);
        var blockers = Evaluate(request.OperationId, folder);
        if (blockers.Count > 0)
            return new SetupOutcome(false, null, null, blockers);

        return await handlers.Run(runner, folderPath, folder, ct);
    }

    private static string[] InitArgs() => new[] { "init", "-b", "main" };

    private static SetupOutcome WriteGitignore(FolderState folder)
    {
        var target = Path.Combine(folder.Path, ".gitignore");

        try
        {
            // CreateNew rather than WriteAllText: the blocker check above can race with the
            // user's editor, and losing a file they curated is not undoable by anything the
            // app can show them.
            using var stream = new FileStream(target, FileMode.CreateNew, FileAccess.Write);
            using var writer = new StreamWriter(stream);
            writer.Write(GitignoreTemplates.For(folder.ProjectType));
        }
        catch (IOException)
        {
            // CreateNew throws IOException both when the file already exists and for
            // unrelated failures (a locked file, a missing directory, a partial write). Only
            // the first of those is actually true, so the message must check reality rather
            // than assume it — this is the one place the app promises never to touch a
            // user's file, and telling them something false about their own filesystem is
            // worse than telling them nothing.
            return new SetupOutcome(false, null, null, new[]
            {
                File.Exists(target)
                    ? "There is already a .gitignore here, so nothing was written. "
                      + "Open it and add any lines you want rather than replacing it."
                    : "That file could not be written, so nothing was created. "
                      + "Check that this app can write to this folder and try again.",
            });
        }

        return new SetupOutcome(
            Success: true,
            Narration: "Created .gitignore. Git will leave the listed files alone from now on.",
            Error: null,
            Blockers: Array.Empty<string>());
    }

    private static IReadOnlyList<string> Evaluate(string operationId, FolderState folder)
    {
        if (operationId == InitRepository && folder.IsRepository)
        {
            return new[]
            {
                "This folder is already a git project, so there is nothing to set up.",
            };
        }

        if (operationId == CreateGitignore && folder.HasGitignore)
        {
            return new[]
            {
                "There is already a .gitignore in this folder. "
                + "This app will not replace it, because your own rules would be lost.",
            };
        }

        return Array.Empty<string>();
    }

    private static OperationHandlers Require(string operationId)
    {
        if (!Operations.TryGetValue(operationId, out var handlers))
            throw new ArgumentException($"Unknown setup operation '{operationId}'.", nameof(operationId));

        return handlers;
    }

    private readonly record struct PreviewContent(string? CommandLine, string? FileContents);

    private sealed record OperationHandlers(
        Func<FolderState, PreviewContent> Preview,
        Func<IGitRunner, string, FolderState, CancellationToken, Task<SetupOutcome>> Run);
}
