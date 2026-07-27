namespace GitHelper.Core.Model;

/// <summary>
/// What kind of project a folder looks like, used only to pick a .gitignore template.
/// Deliberately coarse: the app ships one short, commented template per member, and every
/// member must map to one.
/// </summary>
public enum ProjectType
{
    Generic,
    DotNet,
    Node,
    Python,
    Java,
}
