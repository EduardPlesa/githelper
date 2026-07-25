using System.Reflection;

namespace GitHelper.Content;

/// <summary>Marker giving the core library a handle on the assembly holding the content files.</summary>
public static class ContentAssembly
{
    public static Assembly Value => typeof(ContentAssembly).Assembly;
}
