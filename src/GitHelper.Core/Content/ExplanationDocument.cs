using GitHelper.Core.Actions;

namespace GitHelper.Core.Content;

/// <summary>One authored action explanation, parsed.</summary>
public sealed record ExplanationDocument(
    string Id,
    string Title,
    Danger Danger,
    IReadOnlyList<string> Terms,
    string? UndoActionId,
    IReadOnlyList<ContentBlock> What,
    IReadOnlyList<ContentBlock> Risks,
    IReadOnlyList<ContentBlock> Undo);

/// <summary>Thrown when a content file does not match the schema. Always names its source file.</summary>
public sealed class ContentException(string message) : Exception(message);
