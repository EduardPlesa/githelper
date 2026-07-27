namespace GitHelper.Core.Content;

/// <summary>
/// A closed schema. The UI renders exactly these cases, so any content the parser
/// cannot express is a content error rather than something silently dropped.
/// </summary>
public abstract record ContentBlock;

public sealed record ParagraphBlock(IReadOnlyList<InlineSpan> Spans) : ContentBlock;

public sealed record BulletListBlock(IReadOnlyList<IReadOnlyList<InlineSpan>> Items) : ContentBlock;

public sealed record CodeBlock(string Text) : ContentBlock;

public abstract record InlineSpan;

public sealed record TextSpan(string Text) : InlineSpan;

public sealed record CodeSpan(string Text) : InlineSpan;

/// <summary>
/// Emphasis, written as **bold** in content files. Reserved for the sentences a beginner
/// must not skim past — chiefly the consequence line on the one destructive action.
/// </summary>
public sealed record StrongSpan(string Text) : InlineSpan;

/// <summary>A glossary reference. The UI underlines it and shows the definition on hover.</summary>
public sealed record TermSpan(string TermId, string Display) : InlineSpan;

/// <summary>A placeholder filled from RepoState at render time.</summary>
public sealed record SlotSpan(string SlotName) : InlineSpan;
