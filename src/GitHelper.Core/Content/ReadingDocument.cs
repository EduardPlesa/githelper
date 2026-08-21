namespace GitHelper.Core.Content;

/// <summary>
/// Authored prose for a surface the user reads rather than an action they take.
///
/// Deliberately not an ExplanationDocument: a read surface has no danger level, no risks and
/// nothing to undo, and forcing it into that shape would ship three empty sections and a
/// 'danger' that describes nothing.
/// </summary>
public sealed record ReadingDocument(
    string Id,
    string Title,
    IReadOnlyList<string> Terms,
    IReadOnlyList<ContentBlock> What);
