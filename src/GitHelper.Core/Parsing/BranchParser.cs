using GitHelper.Core.Model;

namespace GitHelper.Core.Parsing;

/// <summary>Parses the branch format produced by <see cref="Format"/>.</summary>
public static class BranchParser
{
    /// <summary>
    /// A tab is a safe separator here: git rejects control characters in refnames,
    /// so no branch name can contain one.
    /// </summary>
    public const string Format = "%(refname:short)%09%(upstream:short)";

    public static IReadOnlyList<BranchInfo> Parse(string output)
    {
        var branches = new List<BranchInfo>();

        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.Length == 0) continue;

            var fields = trimmed.Split('\t');
            var name = fields[0];
            if (name.Length == 0) continue;

            // %(upstream:short) expands to empty when no upstream is configured.
            var upstream = fields.Length > 1 && fields[1].Length > 0 ? fields[1] : null;
            branches.Add(new BranchInfo(name, upstream));
        }

        return branches;
    }
}
