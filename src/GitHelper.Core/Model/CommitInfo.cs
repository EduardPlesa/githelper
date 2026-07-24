namespace GitHelper.Core.Model;

public sealed record CommitInfo(
    string Hash,
    string ShortHash,
    string Author,
    DateTimeOffset Date,
    string Subject);
