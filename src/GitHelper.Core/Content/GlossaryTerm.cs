namespace GitHelper.Core.Content;

/// <summary>
/// One glossary definition. Defined exactly once and referenced by id everywhere, so
/// correcting a poor explanation corrects it in every place it appears.
/// </summary>
public sealed record GlossaryTerm(
    string Id,
    string Title,
    IReadOnlyList<ContentBlock> Definition);
