# Local Repository Setup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a beginner turn a folder that is not a git repository into one, and offer a `.gitignore` chosen for the kind of project found there.

**Architecture:** A small `SetupService` over a new `FolderState` runs the two operations that cannot be ordinary `GitAction`s — `init-repository` (no `RepoState` exists yet) and `create-gitignore` (not a git command at all). Both flow through the existing explain panel, which grows a "The file" variant of its command heading. The startup screen's dead-end "not a git project" error becomes the entry point.

**Tech Stack:** .NET 10, Avalonia 11.3, CommunityToolkit.Mvvm 8.4, xUnit.

**Spec:** [docs/superpowers/specs/2026-07-28-local-repository-setup-design.md](../specs/2026-07-28-local-repository-setup-design.md)

## Global Constraints

- **No credentials, ever.** No view may contain a password or token field. This plan touches no authentication.
- **argv only.** Every git invocation is a `string[]` through `IGitRunner`. Never a joined string, never a shell.
- **Never overwrite a user's `.gitignore`.** Refuse and explain instead.
- **No automatic commits.** Nothing in this plan creates a commit the user did not ask for.
- **Setup content uses no slots.** `SlotBinder.Bind` needs a `RepoState`, which does not exist before `init`. Task 3 adds a test enforcing this.
- **`GitHelper.Core` has no Avalonia reference.** Views may touch Avalonia and engine types; viewmodels may not touch Avalonia.
- **Warnings are errors** (`TreatWarningsAsErrors`) in every project.

---

### Task 1: Folder inspection

**Files:**
- Create: `src/GitHelper.Core/Model/ProjectType.cs`
- Create: `src/GitHelper.Core/Model/FolderState.cs`
- Create: `src/GitHelper.Core/Repo/FolderInspector.cs`
- Test: `tests/GitHelper.Core.Tests/FolderInspectorTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:
  - `enum ProjectType { Generic, DotNet, Node, Python, Java }`
  - `sealed record FolderState(string Path, bool IsRepository, int FileCount, bool HasGitignore, ProjectType ProjectType)`
  - `sealed class FolderInspector` with `FolderState Inspect(string folderPath)`

**Why detection is a pure function of a directory listing.** It never runs git and never reads file contents, so it is fast enough to run on every refresh and trivially testable against a temp directory.

- [ ] **Step 1: Write the failing test**

Create `tests/GitHelper.Core.Tests/FolderInspectorTests.cs`:

```csharp
using GitHelper.Core.Model;
using GitHelper.Core.Repo;

namespace GitHelper.Core.Tests;

public class FolderInspectorTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "githelper-inspect-" + Guid.NewGuid().ToString("N"));

    public FolderInspectorTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private void Write(string name, string content = "x")
    {
        var full = Path.Combine(_dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    [Fact]
    public void AnEmptyFolderIsNotARepositoryAndHasNoFiles()
    {
        var state = new FolderInspector().Inspect(_dir);

        Assert.False(state.IsRepository);
        Assert.Equal(0, state.FileCount);
        Assert.False(state.HasGitignore);
        Assert.Equal(ProjectType.Generic, state.ProjectType);
    }

    [Fact]
    public void ADotGitDirectoryMakesItARepository()
    {
        Directory.CreateDirectory(Path.Combine(_dir, ".git"));

        Assert.True(new FolderInspector().Inspect(_dir).IsRepository);
    }

    [Fact]
    public void AnExistingGitignoreIsReported()
    {
        Write(".gitignore", "bin/\n");

        Assert.True(new FolderInspector().Inspect(_dir).HasGitignore);
    }

    [Theory]
    [InlineData("App.csproj", ProjectType.DotNet)]
    [InlineData("package.json", ProjectType.Node)]
    [InlineData("main.py", ProjectType.Python)]
    [InlineData("pom.xml", ProjectType.Java)]
    [InlineData("build.gradle", ProjectType.Java)]
    [InlineData("notes.txt", ProjectType.Generic)]
    public void ProjectTypeIsDetectedFromTheFilesPresent(string fileName, ProjectType expected)
    {
        Write(fileName);

        Assert.Equal(expected, new FolderInspector().Inspect(_dir).ProjectType);
    }

    [Fact]
    public void DetectionLooksOneLevelDownAsWell()
    {
        // Solutions commonly keep projects in subfolders, with nothing telling at the root.
        Write("src/App/App.csproj");

        Assert.Equal(ProjectType.DotNet, new FolderInspector().Inspect(_dir).ProjectType);
    }

    [Fact]
    public void FileCountIgnoresTheGitDirectory()
    {
        // .git holds hundreds of files; counting them would tell the user nothing true.
        Directory.CreateDirectory(Path.Combine(_dir, ".git"));
        Write(".git/config", "[core]\n");
        Write("readme.txt");

        Assert.Equal(1, new FolderInspector().Inspect(_dir).FileCount);
    }

    [Fact]
    public void AMissingFolderIsReportedAsEmptyRatherThanThrowing()
    {
        var missing = Path.Combine(_dir, "nope");

        var state = new FolderInspector().Inspect(missing);

        Assert.False(state.IsRepository);
        Assert.Equal(0, state.FileCount);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/GitHelper.Core.Tests/GitHelper.Core.Tests.csproj --filter FolderInspectorTests`
Expected: FAIL — `ProjectType`, `FolderState`, `FolderInspector` do not exist (CS0246).

- [ ] **Step 3: Write `ProjectType`**

Create `src/GitHelper.Core/Model/ProjectType.cs`:

```csharp
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
```

- [ ] **Step 4: Write `FolderState`**

Create `src/GitHelper.Core/Model/FolderState.cs`:

```csharp
namespace GitHelper.Core.Model;

/// <summary>
/// What can be known about a folder without git. This is the pre-repository domain: before
/// `git init` there is no branch, no commits and no upstream, so RepoState cannot describe it.
/// </summary>
public sealed record FolderState(
    string Path,
    bool IsRepository,
    int FileCount,
    bool HasGitignore,
    ProjectType ProjectType);
```

- [ ] **Step 5: Write `FolderInspector`**

Create `src/GitHelper.Core/Repo/FolderInspector.cs`:

```csharp
using GitHelper.Core.Model;

namespace GitHelper.Core.Repo;

/// <summary>
/// Reads a folder without running git. Pure enough to run on every refresh, and testable
/// against a temp directory with no repository involved.
/// </summary>
public sealed class FolderInspector
{
    public FolderState Inspect(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            return new FolderState(folderPath, false, 0, false, ProjectType.Generic);

        var isRepository = Directory.Exists(Path.Combine(folderPath, ".git"));
        var names = SafeList(folderPath);

        return new FolderState(
            Path: folderPath,
            IsRepository: isRepository,
            FileCount: names.Count,
            HasGitignore: File.Exists(Path.Combine(folderPath, ".gitignore")),
            ProjectType: Detect(names));
    }

    /// <summary>
    /// Files at the root plus one level down. Solutions routinely keep every project in a
    /// subfolder, leaving nothing telling at the root — and going deeper would mean walking
    /// node_modules on every refresh.
    /// </summary>
    private static List<string> SafeList(string folderPath)
    {
        var names = new List<string>();

        try
        {
            foreach (var file in Directory.EnumerateFiles(folderPath))
                names.Add(Path.GetFileName(file));

            foreach (var directory in Directory.EnumerateDirectories(folderPath))
            {
                if (string.Equals(Path.GetFileName(directory), ".git", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    foreach (var file in Directory.EnumerateFiles(directory))
                        names.Add(Path.GetFileName(file));
                }
                catch (UnauthorizedAccessException)
                {
                    // A folder we cannot read tells us nothing; it must not break inspection.
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }

        return names;
    }

    private static ProjectType Detect(IReadOnlyList<string> names)
    {
        // Ordered by how conclusive the marker is. A csproj means .NET even if a stray .py
        // sits beside it.
        if (names.Any(n => n.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                        || n.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
                        || n.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase)))
            return ProjectType.DotNet;

        if (names.Any(n => n.Equals("package.json", StringComparison.OrdinalIgnoreCase)))
            return ProjectType.Node;

        if (names.Any(n => n.Equals("pom.xml", StringComparison.OrdinalIgnoreCase)
                        || n.StartsWith("build.gradle", StringComparison.OrdinalIgnoreCase)))
            return ProjectType.Java;

        if (names.Any(n => n.Equals("requirements.txt", StringComparison.OrdinalIgnoreCase)
                        || n.Equals("pyproject.toml", StringComparison.OrdinalIgnoreCase)
                        || n.EndsWith(".py", StringComparison.OrdinalIgnoreCase)))
            return ProjectType.Python;

        return ProjectType.Generic;
    }
}
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/GitHelper.Core.Tests/GitHelper.Core.Tests.csproj --filter FolderInspectorTests`
Expected: PASS, 11 tests.

- [ ] **Step 7: Commit**

```bash
git add src/GitHelper.Core/Model/ProjectType.cs src/GitHelper.Core/Model/FolderState.cs src/GitHelper.Core/Repo/FolderInspector.cs tests/GitHelper.Core.Tests/FolderInspectorTests.cs
git commit -m "feat: inspect a folder before any repository exists"
```

---

### Task 2: `.gitignore` templates

**Files:**
- Create: `src/GitHelper.Content/gitignore/generic.gitignore`
- Create: `src/GitHelper.Content/gitignore/dotnet.gitignore`
- Create: `src/GitHelper.Content/gitignore/node.gitignore`
- Create: `src/GitHelper.Content/gitignore/python.gitignore`
- Create: `src/GitHelper.Content/gitignore/java.gitignore`
- Modify: `src/GitHelper.Content/GitHelper.Content.csproj`
- Create: `src/GitHelper.Core/Content/GitignoreTemplates.cs`
- Test: `tests/GitHelper.Core.Tests/GitignoreTemplateTests.cs`

**Interfaces:**
- Consumes: `ProjectType` (Task 1).
- Produces: `static class GitignoreTemplates` with `static string For(ProjectType type)`.

**Every line is commented, because the app has to explain the file.** A template the user cannot read defeats the point of showing it to them.

- [ ] **Step 1: Write the failing test**

Create `tests/GitHelper.Core.Tests/GitignoreTemplateTests.cs`:

```csharp
using GitHelper.Core.Content;
using GitHelper.Core.Model;

namespace GitHelper.Core.Tests;

public class GitignoreTemplateTests
{
    [Theory]
    [InlineData(ProjectType.Generic)]
    [InlineData(ProjectType.DotNet)]
    [InlineData(ProjectType.Node)]
    [InlineData(ProjectType.Python)]
    [InlineData(ProjectType.Java)]
    public void EveryProjectTypeHasATemplate(ProjectType type)
    {
        var text = GitignoreTemplates.For(type);

        Assert.False(string.IsNullOrWhiteSpace(text));
    }

    [Theory]
    [InlineData(ProjectType.Generic)]
    [InlineData(ProjectType.DotNet)]
    [InlineData(ProjectType.Node)]
    [InlineData(ProjectType.Python)]
    [InlineData(ProjectType.Java)]
    public void EveryTemplateIsCommentedSoItCanBeExplained(ProjectType type)
    {
        var lines = GitignoreTemplates.For(type)
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

        Assert.Contains(lines, l => l.StartsWith('#'));
        // Every rule needs a comment somewhere above it; a wall of bare globs is unteachable.
        var rules = lines.Count(l => !l.StartsWith('#'));
        var comments = lines.Count(l => l.StartsWith('#'));
        Assert.True(comments >= rules / 3,
            $"{type}: {rules} rules but only {comments} comments");
    }

    [Fact]
    public void TheDotNetTemplateIgnoresBuildOutput()
    {
        var text = GitignoreTemplates.For(ProjectType.DotNet);

        Assert.Contains("bin/", text);
        Assert.Contains("obj/", text);
    }

    [Fact]
    public void TheNodeTemplateIgnoresDependencies()
    {
        Assert.Contains("node_modules/", GitignoreTemplates.For(ProjectType.Node));
    }

    [Fact]
    public void EveryTemplateIgnoresEnvironmentFiles()
    {
        // Committing a .env is the single most common way a beginner leaks a secret.
        foreach (var type in Enum.GetValues<ProjectType>())
            Assert.Contains(".env", GitignoreTemplates.For(type));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/GitHelper.Core.Tests/GitHelper.Core.Tests.csproj --filter GitignoreTemplateTests`
Expected: FAIL — `GitignoreTemplates` does not exist (CS0246).

- [ ] **Step 3: Write the templates**

Create `src/GitHelper.Content/gitignore/generic.gitignore`:

```gitignore
# Files git should leave alone.
# Anything listed here stays on your computer and is never committed.

# Secrets. Never commit these -- anyone who can see the repository can read them.
.env
.env.*
*.pem
*.key

# Junk your operating system leaves behind.
Thumbs.db
Desktop.ini
.DS_Store

# Editor and tool settings that are yours, not the project's.
.vscode/
.idea/
*.swp

# Logs and temporary files.
*.log
tmp/
```

Create `src/GitHelper.Content/gitignore/dotnet.gitignore`:

```gitignore
# Files git should leave alone, for a .NET project.

# Build output. Rebuilt from source every time, so committing it only causes conflicts.
bin/
obj/

# Visual Studio and Rider settings that belong to you, not the project.
.vs/
.idea/
*.user
*.suo

# Secrets. Never commit these.
.env
.env.*
appsettings.Development.json
*.pfx

# Test and coverage output.
TestResults/
coverage/

# Operating system junk.
Thumbs.db
.DS_Store
```

Create `src/GitHelper.Content/gitignore/node.gitignore`:

```gitignore
# Files git should leave alone, for a Node project.

# Installed dependencies. Huge, and rebuilt by `npm install` from package.json.
node_modules/

# Build output.
dist/
build/
.next/
out/

# Secrets. Never commit these.
.env
.env.*

# Logs.
npm-debug.log*
yarn-error.log*
*.log

# Editor settings and operating system junk.
.vscode/
.idea/
.DS_Store
Thumbs.db
```

Create `src/GitHelper.Content/gitignore/python.gitignore`:

```gitignore
# Files git should leave alone, for a Python project.

# Compiled bytecode, regenerated automatically.
__pycache__/
*.py[cod]

# Virtual environments. Large, and specific to your machine.
.venv/
venv/
env/

# Secrets. Never commit these.
.env
.env.*

# Packaging and test output.
dist/
build/
*.egg-info/
.pytest_cache/
.coverage

# Editor settings and operating system junk.
.vscode/
.idea/
.DS_Store
Thumbs.db
```

Create `src/GitHelper.Content/gitignore/java.gitignore`:

```gitignore
# Files git should leave alone, for a Java project.

# Compiled classes and build output.
*.class
target/
build/
out/

# Gradle and Maven working directories.
.gradle/
!gradle/wrapper/gradle-wrapper.jar

# Secrets. Never commit these.
.env
.env.*
*.jks

# Editor settings and operating system junk.
.idea/
*.iml
.vscode/
.DS_Store
Thumbs.db
```

- [ ] **Step 4: Embed the templates**

In `src/GitHelper.Content/GitHelper.Content.csproj`, replace the existing `ItemGroup`:

```xml
  <ItemGroup>
    <EmbeddedResource Include="actions/**/*.md" />
    <EmbeddedResource Include="terms/**/*.md" />
    <EmbeddedResource Include="gitignore/**/*.gitignore" />
  </ItemGroup>
```

- [ ] **Step 5: Write the loader**

Create `src/GitHelper.Core/Content/GitignoreTemplates.cs`:

```csharp
using System.Reflection;
using GitHelper.Core.Model;

namespace GitHelper.Core.Content;

/// <summary>
/// The shipped .gitignore templates, one per <see cref="ProjectType"/>. Loaded once: they are
/// embedded in the assembly and never change at runtime.
/// </summary>
public static class GitignoreTemplates
{
    private static readonly Lazy<IReadOnlyDictionary<ProjectType, string>> Templates =
        new(() => Load(global::GitHelper.Content.ContentAssembly.Value));

    public static string For(ProjectType type) => Templates.Value[type];

    private static IReadOnlyDictionary<ProjectType, string> Load(Assembly assembly)
    {
        var byType = new Dictionary<ProjectType, string>();

        foreach (var type in Enum.GetValues<ProjectType>())
        {
            // Resource names are dotted paths: GitHelper.Content.gitignore.dotnet.gitignore
            var suffix = $".gitignore.{type.ToString().ToLowerInvariant()}.gitignore";
            var name = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                ?? throw new ContentException($"No .gitignore template embedded for '{type}'.");

            using var stream = assembly.GetManifestResourceStream(name)
                ?? throw new ContentException($"{name}: embedded resource could not be opened.");
            using var reader = new StreamReader(stream);
            byType[type] = reader.ReadToEnd().Replace("\r\n", "\n");
        }

        return byType;
    }
}
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/GitHelper.Core.Tests/GitHelper.Core.Tests.csproj --filter GitignoreTemplateTests`
Expected: PASS, 13 tests.

- [ ] **Step 7: Commit**

```bash
git add src/GitHelper.Content/gitignore src/GitHelper.Content/GitHelper.Content.csproj src/GitHelper.Core/Content/GitignoreTemplates.cs tests/GitHelper.Core.Tests/GitignoreTemplateTests.cs
git commit -m "feat: ship a commented .gitignore template per project type"
```

---

### Task 3: Setup content, and keeping the integrity tests honest

**Files:**
- Create: `src/GitHelper.Content/setup/init-repository.md`
- Create: `src/GitHelper.Content/setup/create-gitignore.md`
- Create: `src/GitHelper.Content/terms/local-repository.md`
- Modify: `src/GitHelper.Content/GitHelper.Content.csproj`
- Modify: `src/GitHelper.Core/Content/ContentLibrary.cs`
- Modify: `tests/GitHelper.Core.Tests/ContentIntegrityTests.cs`
- Test: `tests/GitHelper.Core.Tests/SetupContentTests.cs`

**Interfaces:**
- Consumes: `ContentParser`, `ExplanationDocument` (existing).
- Produces: `ContentLibrary.Setup` — `IReadOnlyDictionary<string, ExplanationDocument>`.

**Why setup content lives in its own folder.** `ContentIntegrityTests.EveryContentFileMatchesARealAction` asserts that every document in `Library.Actions` maps to an `ActionCatalog` entry. Setup operations are deliberately *not* in the catalogue, so putting their content under `actions/` would fail that test — and loosening it would give up a real guarantee about ordinary actions. A separate `setup/` folder keeps both invariants intact.

**Two existing tests must be widened, or they will fail.** `EveryGlossaryTermIsActuallyReferencedSomewhere` scans only `Library.Actions`; `local-repository` is referenced only from setup content, so it would be reported as unused. `EveryInlineTermReferenceResolvesToAGlossaryFile` and `EverySlotIsInTheKnownVocabulary` should cover setup documents for the same reason they cover actions.

- [ ] **Step 1: Write the failing test**

Create `tests/GitHelper.Core.Tests/SetupContentTests.cs`:

```csharp
using GitHelper.Core.Content;

namespace GitHelper.Core.Tests;

public class SetupContentTests
{
    private static readonly ContentLibrary Library = ContentLibrary.Load();

    [Theory]
    [InlineData("init-repository")]
    [InlineData("create-gitignore")]
    public void SetupOperationsHaveContent(string id)
    {
        Assert.True(Library.Setup.ContainsKey(id), $"no setup content for '{id}'");
    }

    [Theory]
    [InlineData("init-repository")]
    [InlineData("create-gitignore")]
    public void SetupContentFillsAllFourHeadings(string id)
    {
        var document = Library.Setup[id];

        Assert.NotEmpty(document.What);
        Assert.NotEmpty(document.Risks);
        Assert.NotEmpty(document.Undo);
        Assert.False(string.IsNullOrWhiteSpace(document.Title));
    }

    [Fact]
    public void SetupContentUsesNoSlots()
    {
        // SlotBinder.Bind needs a RepoState, which does not exist before `git init`. A slot
        // here would reach the renderer unresolved and throw.
        var offenders = Library.Setup.Values
            .SelectMany(d => d.What.Concat(d.Risks).Concat(d.Undo))
            .OfType<ParagraphBlock>()
            .SelectMany(p => p.Spans)
            .OfType<SlotSpan>()
            .Select(s => s.SlotName)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void SetupContentIsNotAlsoLoadedAsAnAction()
    {
        // Otherwise EveryContentFileMatchesARealAction would fail: these have no catalogue entry.
        Assert.False(Library.Actions.ContainsKey("init-repository"));
        Assert.False(Library.Actions.ContainsKey("create-gitignore"));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/GitHelper.Core.Tests/GitHelper.Core.Tests.csproj --filter SetupContentTests`
Expected: FAIL — `ContentLibrary` has no `Setup` property (CS1061).

- [ ] **Step 3: Write the content files**

Create `src/GitHelper.Content/setup/init-repository.md`:

```markdown
---
id: init-repository
title: Start tracking this folder
danger: safe
terms:
  - local-repository
  - commit
---

## what

Creates a [[local-repository|local repository]] in this folder: a hidden `.git` folder where
git stores every version you save from now on.

Your files are not moved, renamed, or changed. Nothing is saved into history yet either —
that happens when you make your first [[commit]].

The new repository starts on a branch called `main`.

## risks

Almost none. Nothing outside the new `.git` folder is touched, and no file you already have is
read, changed, or deleted.

The history starts empty. Git does not go back and record the versions of your files that
existed before this moment, because it never saw them.

## undo

Delete the `.git` folder that was just created. Your own files are untouched by that, and the
folder goes back to being an ordinary folder.
```

Create `src/GitHelper.Content/setup/create-gitignore.md`:

```markdown
---
id: create-gitignore
title: Set up a .gitignore
danger: safe
terms:
  - commit
---

## what

Creates a file called `.gitignore` listing the things git should leave alone — build output,
installed dependencies, editor settings, and secrets.

Anything matching a line in that file stays on your computer and never goes into a [[commit]],
so it never reaches anyone you share the project with.

The file is ordinary text. You can open and edit it whenever you like.

## risks

Files already being tracked by git are not affected by adding them here. `.gitignore` decides
what git *starts* paying attention to, not what it already watches.

If a rule is too broad you might hide a file you meant to save. Everything in the file is
commented, so you can read what each line does before it is written.

## undo

Delete the `.gitignore` file, or open it and remove the lines you do not want.
```

Create `src/GitHelper.Content/terms/local-repository.md`:

```markdown
---
id: local-repository
title: Local repository
---

## definition

The copy of a project's history that lives on your own computer, inside a hidden `.git` folder
next to your files.

It is complete on its own. Every version you have saved is there, and you can look back through
all of it with no internet connection and no account anywhere.

"Local" is there to distinguish it from a copy kept on a server. Until you deliberately send it
somewhere, this is the only copy that exists.
```

- [ ] **Step 4: Embed the setup folder**

In `src/GitHelper.Content/GitHelper.Content.csproj`, add to the `ItemGroup` from Task 2:

```xml
    <EmbeddedResource Include="setup/**/*.md" />
```

- [ ] **Step 5: Load setup content**

In `src/GitHelper.Core/Content/ContentLibrary.cs`, add the property and constructor parameter:

```csharp
    public IReadOnlyDictionary<string, ExplanationDocument> Actions { get; }
    public IReadOnlyDictionary<string, ExplanationDocument> Setup { get; }
    public IReadOnlyDictionary<string, GlossaryTerm> Terms { get; }

    private ContentLibrary(
        IReadOnlyDictionary<string, ExplanationDocument> actions,
        IReadOnlyDictionary<string, ExplanationDocument> setup,
        IReadOnlyDictionary<string, GlossaryTerm> terms)
    {
        Actions = actions;
        Setup = setup;
        Terms = terms;
    }
```

In `Load(Assembly)`, add the dictionary, the branch, and the updated return:

```csharp
        var actions = new Dictionary<string, ExplanationDocument>(StringComparer.OrdinalIgnoreCase);
        var setup = new Dictionary<string, ExplanationDocument>(StringComparer.OrdinalIgnoreCase);
        var terms = new Dictionary<string, GlossaryTerm>(StringComparer.OrdinalIgnoreCase);
```

```csharp
            else if (resourceName.Contains(".setup.", StringComparison.OrdinalIgnoreCase))
            {
                var document = ContentParser.Parse(text, resourceName);
                if (setup.ContainsKey(document.Id))
                    throw new ContentException($"{resourceName}: duplicate setup id '{document.Id}'.");
                setup[document.Id] = document;
            }
```

Place that branch **after** the `.actions.` branch and **before** the `.terms.` branch. Then:

```csharp
        return new ContentLibrary(actions, setup, terms);
```

- [ ] **Step 6: Widen the integrity tests**

In `tests/GitHelper.Core.Tests/ContentIntegrityTests.cs`, add a helper beside the existing ones:

```csharp
    private static IEnumerable<ExplanationDocument> AllDocuments()
        => Library.Actions.Values.Concat(Library.Setup.Values);
```

Then replace the bodies of these three tests so they cover setup documents too:

```csharp
    [Fact]
    public void EveryDeclaredTermResolvesToAGlossaryFile()
    {
        var unresolved = AllDocuments()
            .SelectMany(d => d.Terms.Select(t => (Document: d.Id, Term: t)))
            .Where(x => !Library.Terms.ContainsKey(x.Term))
            .ToList();

        Assert.Empty(unresolved);
    }

    [Fact]
    public void EveryInlineTermReferenceResolvesToAGlossaryFile()
    {
        var unresolved = AllDocuments()
            .SelectMany(d => AllSpans(d).OfType<TermSpan>().Select(s => (Document: d.Id, s.TermId)))
            .Where(x => !Library.Terms.ContainsKey(x.TermId))
            .ToList();

        Assert.Empty(unresolved);
    }

    [Fact]
    public void EveryGlossaryTermIsActuallyReferencedSomewhere()
    {
        var referenced = AllDocuments()
            .SelectMany(d => d.Terms.Concat(AllSpans(d).OfType<TermSpan>().Select(s => s.TermId)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unused = Library.Terms.Keys.Where(id => !referenced.Contains(id)).ToList();

        Assert.Empty(unused);
    }
```

Leave `EveryContentFileMatchesARealAction` scanning `Library.Actions` only — that is the invariant it exists to protect.

- [ ] **Step 7: Run the content tests**

Run: `dotnet test tests/GitHelper.Core.Tests/GitHelper.Core.Tests.csproj --filter "SetupContentTests|ContentIntegrityTests|ContentLibraryTests"`
Expected: PASS. If `EveryGlossaryTermIsActuallyReferencedSomewhere` fails, the `AllDocuments` change in Step 6 was not applied.

- [ ] **Step 8: Commit**

```bash
git add src/GitHelper.Content/setup src/GitHelper.Content/terms/local-repository.md src/GitHelper.Content/GitHelper.Content.csproj src/GitHelper.Core/Content/ContentLibrary.cs tests/GitHelper.Core.Tests
git commit -m "feat: load setup content separately from action content"
```

---

### Task 4: `SetupService` and `init-repository`

**Files:**
- Create: `src/GitHelper.Core/Setup/SetupRequest.cs`
- Create: `src/GitHelper.Core/Setup/SetupPreview.cs`
- Create: `src/GitHelper.Core/Setup/SetupOutcome.cs`
- Create: `src/GitHelper.Core/Setup/SetupService.cs`
- Test: `tests/GitHelper.Core.Tests/SetupServiceInitTests.cs`

**Interfaces:**
- Consumes: `FolderInspector`, `FolderState` (Task 1); `ContentLibrary.Setup` (Task 3); `IGitRunner`, `GitCommandResult`, `ErrorTranslator` (existing).
- Produces:
  - `sealed record SetupRequest(string OperationId)`
  - `sealed record SetupPreview(string OperationId, string Title, ExplanationDocument Explanation, string? CommandLine, string? FileContents, IReadOnlyList<string> Blockers)` with `bool CanRun => Blockers.Count == 0`
  - `sealed record SetupOutcome(bool Success, string? Narration, TranslatedError? Error, IReadOnlyList<string> Blockers)`
  - `sealed class SetupService(IGitRunner runner, FolderInspector inspector, ContentLibrary content)` with `Task<SetupPreview> PreviewAsync(string folderPath, SetupRequest request, CancellationToken ct = default)` and `Task<SetupOutcome> RunAsync(string folderPath, SetupRequest request, CancellationToken ct = default)`

**Blockers are plain strings here, not `PreconditionResult`.** The setup domain has two operations and no shared precondition vocabulary; borrowing the action machinery would mean dragging `RepoState` into a place that has none.

- [ ] **Step 1: Write the failing test**

Create `tests/GitHelper.Core.Tests/SetupServiceInitTests.cs`:

```csharp
using GitHelper.Core.Content;
using GitHelper.Core.Git;
using GitHelper.Core.Repo;
using GitHelper.Core.Setup;

namespace GitHelper.Core.Tests;

public class SetupServiceInitTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "githelper-setup-" + Guid.NewGuid().ToString("N"));

    public SetupServiceInitTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static SetupService NewService() =>
        new(new GitRunner(), new FolderInspector(), ContentLibrary.Load());

    [Fact]
    public async Task PreviewShowsTheCommandAndRunsNothing()
    {
        var preview = await NewService().PreviewAsync(_dir, new SetupRequest("init-repository"));

        Assert.Equal("init -b main", string.Join(' ', preview.CommandLine!.Split(' ').Skip(1)));
        Assert.Null(preview.FileContents);
        Assert.True(preview.CanRun);
        Assert.NotEmpty(preview.Explanation.What);
        Assert.False(Directory.Exists(Path.Combine(_dir, ".git")));
    }

    [Fact]
    public async Task RunCreatesARepositoryOnMain()
    {
        var outcome = await NewService().RunAsync(_dir, new SetupRequest("init-repository"));

        Assert.True(outcome.Success);
        Assert.True(Directory.Exists(Path.Combine(_dir, ".git")));

        var branch = await new GitRunner().RunAsync(_dir, new[] { "branch", "--show-current" });
        Assert.Equal("main", branch.StdOut.Trim());
    }

    [Fact]
    public async Task PreviewIsBlockedWhenTheFolderIsAlreadyARepository()
    {
        await new GitRunner().RunAsync(_dir, new[] { "init", "-q", "-b", "main" });

        var preview = await NewService().PreviewAsync(_dir, new SetupRequest("init-repository"));

        Assert.False(preview.CanRun);
        Assert.Contains(preview.Blockers, b => b.Contains("already", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunRefusesWhenTheFolderBecameARepositoryAfterThePreview()
    {
        var service = NewService();
        var preview = await service.PreviewAsync(_dir, new SetupRequest("init-repository"));
        Assert.True(preview.CanRun);

        // Someone ran `git init` in a terminal meanwhile.
        await new GitRunner().RunAsync(_dir, new[] { "init", "-q", "-b", "main" });

        var outcome = await service.RunAsync(_dir, new SetupRequest("init-repository"));

        Assert.False(outcome.Success);
        Assert.NotEmpty(outcome.Blockers);
    }

    [Fact]
    public async Task AnUnknownOperationIsRejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => NewService().PreviewAsync(_dir, new SetupRequest("nonsense")));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/GitHelper.Core.Tests/GitHelper.Core.Tests.csproj --filter SetupServiceInitTests`
Expected: FAIL — the `GitHelper.Core.Setup` namespace does not exist (CS0246).

- [ ] **Step 3: Write the records**

Create `src/GitHelper.Core/Setup/SetupRequest.cs`:

```csharp
namespace GitHelper.Core.Setup;

/// <summary>Names a setup operation. There are no parameters yet; none of them need any.</summary>
public sealed record SetupRequest(string OperationId);
```

Create `src/GitHelper.Core/Setup/SetupPreview.cs`:

```csharp
using GitHelper.Core.Content;

namespace GitHelper.Core.Setup;

/// <summary>
/// Everything the explain panel needs for a setup operation, produced without changing
/// anything. Exactly one of CommandLine and FileContents is non-null: `init` runs a command,
/// `create-gitignore` writes a file and has no command to show.
/// </summary>
public sealed record SetupPreview(
    string OperationId,
    string Title,
    ExplanationDocument Explanation,
    string? CommandLine,
    string? FileContents,
    IReadOnlyList<string> Blockers)
{
    public bool CanRun => Blockers.Count == 0;
}
```

Create `src/GitHelper.Core/Setup/SetupOutcome.cs`:

```csharp
using GitHelper.Core.Errors;

namespace GitHelper.Core.Setup;

/// <summary>The result of running a setup operation.</summary>
public sealed record SetupOutcome(
    bool Success,
    string? Narration,
    TranslatedError? Error,
    IReadOnlyList<string> Blockers);
```

- [ ] **Step 4: Write `SetupService` with `init-repository` only**

Create `src/GitHelper.Core/Setup/SetupService.cs`:

```csharp
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
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/GitHelper.Core.Tests/GitHelper.Core.Tests.csproj --filter SetupServiceInitTests`
Expected: PASS, 5 tests.

If `PreviewShowsTheCommandAndRunsNothing` fails on the command string, print `preview.CommandLine` — `GitCommandResult.CommandLine` prefixes `git ` and quotes arguments containing spaces, and the assertion strips only the leading `git`.

- [ ] **Step 6: Commit**

```bash
git add src/GitHelper.Core/Setup tests/GitHelper.Core.Tests/SetupServiceInitTests.cs
git commit -m "feat: add a setup service that can create a repository"
```

---

### Task 5: `create-gitignore`

**Files:**
- Modify: `src/GitHelper.Core/Setup/SetupService.cs`
- Test: `tests/GitHelper.Core.Tests/SetupServiceGitignoreTests.cs`

**Interfaces:**
- Consumes: `GitignoreTemplates` (Task 2), `SetupService` (Task 4).
- Produces: `SetupService.CreateGitignore` constant; `create-gitignore` handled by the same `PreviewAsync` / `RunAsync`.

- [ ] **Step 1: Write the failing test**

Create `tests/GitHelper.Core.Tests/SetupServiceGitignoreTests.cs`:

```csharp
using GitHelper.Core.Content;
using GitHelper.Core.Git;
using GitHelper.Core.Repo;
using GitHelper.Core.Setup;

namespace GitHelper.Core.Tests;

public class SetupServiceGitignoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "githelper-ignore-" + Guid.NewGuid().ToString("N"));

    public SetupServiceGitignoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static SetupService NewService() =>
        new(new GitRunner(), new FolderInspector(), ContentLibrary.Load());

    private string Path_(string name) => System.IO.Path.Combine(_dir, name);

    [Fact]
    public async Task PreviewShowsTheFileRatherThanACommand()
    {
        File.WriteAllText(Path_("App.csproj"), "<Project />");

        var preview = await NewService().PreviewAsync(_dir, new SetupRequest("create-gitignore"));

        Assert.Null(preview.CommandLine);
        Assert.NotNull(preview.FileContents);
        Assert.Contains("bin/", preview.FileContents!);
        Assert.True(preview.CanRun);
        Assert.False(File.Exists(Path_(".gitignore")));
    }

    [Fact]
    public async Task PreviewPicksTheTemplateForTheDetectedProject()
    {
        File.WriteAllText(Path_("package.json"), "{}");

        var preview = await NewService().PreviewAsync(_dir, new SetupRequest("create-gitignore"));

        Assert.Contains("node_modules/", preview.FileContents!);
    }

    [Fact]
    public async Task RunWritesTheFile()
    {
        File.WriteAllText(Path_("main.py"), "print('hi')");

        var outcome = await NewService().RunAsync(_dir, new SetupRequest("create-gitignore"));

        Assert.True(outcome.Success);
        Assert.Contains("__pycache__/", File.ReadAllText(Path_(".gitignore")));
    }

    [Fact]
    public async Task AnExistingGitignoreIsNeverOverwritten()
    {
        File.WriteAllText(Path_(".gitignore"), "my-own-rules\n");

        var preview = await NewService().PreviewAsync(_dir, new SetupRequest("create-gitignore"));

        Assert.False(preview.CanRun);
        Assert.Contains(preview.Blockers, b => b.Contains("already", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunRefusesIfAGitignoreAppearedAfterThePreview()
    {
        var service = NewService();
        var preview = await service.PreviewAsync(_dir, new SetupRequest("create-gitignore"));
        Assert.True(preview.CanRun);

        File.WriteAllText(Path_(".gitignore"), "my-own-rules\n");

        var outcome = await service.RunAsync(_dir, new SetupRequest("create-gitignore"));

        Assert.False(outcome.Success);
        Assert.NotEmpty(outcome.Blockers);
        Assert.Equal("my-own-rules\n", File.ReadAllText(Path_(".gitignore")));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/GitHelper.Core.Tests/GitHelper.Core.Tests.csproj --filter SetupServiceGitignoreTests`
Expected: FAIL — `ArgumentException: Unknown setup operation 'create-gitignore'`.

- [ ] **Step 3: Extend `SetupService`**

In `src/GitHelper.Core/Setup/SetupService.cs`, replace the constant block:

```csharp
    public const string InitRepository = "init-repository";
    public const string CreateGitignore = "create-gitignore";

    private static readonly string[] KnownOperations = { InitRepository, CreateGitignore };
```

Replace `PreviewAsync`'s body after the blockers are computed:

```csharp
        var document = content.Setup[request.OperationId];

        string? commandLine = null;
        string? fileContents = null;

        if (request.OperationId == InitRepository)
        {
            commandLine = new GitCommandResult(InitArgs(), string.Empty, string.Empty, 0, TimeSpan.Zero)
                .CommandLine;
        }
        else
        {
            fileContents = GitignoreTemplates.For(folder.ProjectType);
        }

        return Task.FromResult(new SetupPreview(
            OperationId: request.OperationId,
            Title: document.Title,
            Explanation: document,
            CommandLine: commandLine,
            FileContents: fileContents,
            Blockers: blockers));
```

Replace `RunAsync`'s body after the blocker check:

```csharp
        if (request.OperationId == CreateGitignore)
            return WriteGitignore(folder);

        var result = await runner.RunAsync(folderPath, InitArgs(), ct);

        return new SetupOutcome(
            Success: result.Success,
            Narration: result.Success
                ? "Started tracking this folder. Git is now watching it for changes."
                : null,
            Error: ErrorTranslator.Translate(result),
            Blockers: Array.Empty<string>());
```

Add the writer and extend `Evaluate`:

```csharp
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
            return new SetupOutcome(false, null, null, new[]
            {
                "There is already a .gitignore here, so nothing was written. "
                + "Open it and add any lines you want rather than replacing it.",
            });
        }

        return new SetupOutcome(
            Success: true,
            Narration: "Created .gitignore. Git will leave the listed files alone from now on.",
            Error: null,
            Blockers: Array.Empty<string>());
    }
```

```csharp
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
```

Add `using GitHelper.Core.Model;` if it is not already present.

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/GitHelper.Core.Tests/GitHelper.Core.Tests.csproj --filter SetupServiceGitignoreTests`
Expected: PASS, 5 tests.

- [ ] **Step 5: Run the whole Core suite**

Run: `dotnet test tests/GitHelper.Core.Tests/GitHelper.Core.Tests.csproj`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/GitHelper.Core/Setup/SetupService.cs tests/GitHelper.Core.Tests/SetupServiceGitignoreTests.cs
git commit -m "feat: offer a .gitignore without ever overwriting one"
```

---

### Task 6: The explain panel shows setup operations

**Files:**
- Modify: `src/GitHelper.App/ViewModels/ExplainPanelViewModel.cs`
- Modify: `src/GitHelper.App/Views/ExplainPanelView.axaml`
- Test: `tests/GitHelper.App.Tests/ExplainPanelSetupTests.cs`

**Interfaces:**
- Consumes: `SetupService`, `SetupRequest`, `SetupPreview`, `SetupOutcome` (Tasks 4–5).
- Produces: on `ExplainPanelViewModel` — `Task ShowSetupAsync(string folderPath, SetupRequest request, CancellationToken ct = default)`, `Task<bool> RunSetupAsync(CancellationToken ct = default)`, and observable `string? FileContents` plus computed `bool HasFileContents`, `bool HasCommandLine`.
- Also produces: `Func<SetupOutcome, CancellationToken, Task>? SetupCompletedAsync`, so the shell can react when a folder becomes a repository.

**One panel, two sources.** The panel keeps a nullable `SetupService` and a nullable pending `SetupRequest`; a setup preview clears the action fields and vice versa, so the two paths can never both be armed.

- [ ] **Step 1: Write the failing test**

Create `tests/GitHelper.App.Tests/ExplainPanelSetupTests.cs`:

```csharp
using GitHelper.App.ViewModels;
using GitHelper.Core.Actions;
using GitHelper.Core.Content;
using GitHelper.Core.Git;
using GitHelper.Core.Repo;
using GitHelper.Core.Setup;

namespace GitHelper.App.Tests;

public class ExplainPanelSetupTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "githelper-panel-setup-" + Guid.NewGuid().ToString("N"));

    public ExplainPanelSetupTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static ExplainPanelViewModel NewPanel()
    {
        var runner = new GitRunner();
        var content = ContentLibrary.Load();
        var actions = new ActionService(runner, new RepoStateReader(runner), content);
        var setup = new SetupService(runner, new FolderInspector(), content);

        return new ExplainPanelViewModel(
            actions, new StubConfirmationDialog(), new InMemorySettingsStore(), setup);
    }

    [Fact]
    public async Task ShowingInitPresentsACommandAndNoFile()
    {
        var panel = NewPanel();

        await panel.ShowSetupAsync(_dir, new SetupRequest("init-repository"));

        Assert.Equal("Start tracking this folder", panel.Title);
        Assert.True(panel.HasCommandLine);
        Assert.False(panel.HasFileContents);
        Assert.NotEmpty(panel.WhatBlocks);
    }

    [Fact]
    public async Task ShowingGitignorePresentsAFileAndNoCommand()
    {
        File.WriteAllText(Path.Combine(_dir, "App.csproj"), "<Project />");
        var panel = NewPanel();

        await panel.ShowSetupAsync(_dir, new SetupRequest("create-gitignore"));

        Assert.True(panel.HasFileContents);
        Assert.False(panel.HasCommandLine);
        Assert.Contains("bin/", panel.FileContents!);
    }

    [Fact]
    public async Task RunningInitCreatesTheRepositoryAndNarrates()
    {
        var panel = NewPanel();
        await panel.ShowSetupAsync(_dir, new SetupRequest("init-repository"));

        var ran = await panel.RunSetupAsync();

        Assert.True(ran);
        Assert.True(Directory.Exists(Path.Combine(_dir, ".git")));
        Assert.False(string.IsNullOrWhiteSpace(panel.Narration));
    }

    [Fact]
    public async Task ABlockedSetupCannotRun()
    {
        await new GitRunner().RunAsync(_dir, new[] { "init", "-q", "-b", "main" });
        var panel = NewPanel();

        await panel.ShowSetupAsync(_dir, new SetupRequest("init-repository"));

        Assert.False(panel.CanRun);
        Assert.True(panel.HasBlockers);
        Assert.False(await panel.RunSetupAsync());
    }

    [Fact]
    public async Task ClearResetsTheFileContents()
    {
        File.WriteAllText(Path.Combine(_dir, "App.csproj"), "<Project />");
        var panel = NewPanel();
        await panel.ShowSetupAsync(_dir, new SetupRequest("create-gitignore"));

        panel.Clear();

        Assert.False(panel.HasFileContents);
        Assert.Null(panel.FileContents);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/GitHelper.App.Tests/GitHelper.App.Tests.csproj --filter ExplainPanelSetupTests`
Expected: FAIL — the constructor has no fourth parameter and `ShowSetupAsync` does not exist.

- [ ] **Step 3: Extend `ExplainPanelViewModel`**

In `src/GitHelper.App/ViewModels/ExplainPanelViewModel.cs`, add `using GitHelper.Core.Setup;`.

Change the constructor to take an optional service and store it, keeping the existing three-argument call sites working:

```csharp
    private readonly SetupService? _setup;
    private SetupRequest? _setupRequest;
    private string? _folderPath;

    public ExplainPanelViewModel(
        ActionService actions,
        IConfirmationDialog confirmations,
        ISettingsStore settings,
        SetupService? setup = null)
    {
        _actions = actions;
        _confirmations = confirmations;
        _settings = settings;
        _setup = setup;

        ConfirmCommand = new AsyncRelayCommand(() => RunAsync(), () => CanRun);
        ToggleTechnicalDetailsCommand = new RelayCommand(
            () => ShowTechnicalDetails = !ShowTechnicalDetails);
    }
```

Add the observable property beside the existing ones:

```csharp
    [ObservableProperty] private string? _fileContents;
```

Add the computed flags and their change hook beside the existing flags:

```csharp
    /// <summary>
    /// True when this operation writes a file instead of running a command — the panel shows
    /// "The file" in place of "The command" rather than inventing a command that does not exist.
    /// </summary>
    public bool HasFileContents => !string.IsNullOrEmpty(FileContents);

    public bool HasCommandLine => !string.IsNullOrEmpty(CommandLine);

    partial void OnFileContentsChanged(string? value)
        => OnPropertyChanged(nameof(HasFileContents));

    partial void OnCommandLineChanged(string value)
        => OnPropertyChanged(nameof(HasCommandLine));
```

Add the setup-completed hook beside `ActionCompletedAsync`:

```csharp
    /// <summary>
    /// Invoked after a setup operation, and awaited. The shell uses it to open a folder that
    /// has just become a repository.
    /// </summary>
    public Func<SetupOutcome, CancellationToken, Task>? SetupCompletedAsync { get; set; }
```

Add the two methods:

```csharp
    /// <summary>Previews a setup operation. Runs nothing.</summary>
    public async Task ShowSetupAsync(
        string folderPath, SetupRequest request, CancellationToken ct = default)
    {
        if (_setup is null)
            throw new InvalidOperationException("This panel was built without a SetupService.");

        var preview = await _setup.PreviewAsync(folderPath, request, ct);

        // Arming the setup path disarms the action path, so the two can never both fire.
        _repoPath = null;
        _request = null;
        _slots = new Dictionary<string, string>();

        _folderPath = folderPath;
        _setupRequest = request;

        Title = preview.Title;
        CommandLine = preview.CommandLine ?? string.Empty;
        FileContents = preview.FileContents;
        DangerLevel = Danger.Safe;
        WhatBlocks = preview.Explanation.What;
        RisksBlocks = preview.Explanation.Risks;
        UndoBlocks = preview.Explanation.Undo;
        Blockers = preview.Blockers.ToArray();
        CanRun = preview.CanRun;
        RequiresConfirmation = true;

        Narration = null;
        Error = null;
        ShowTechnicalDetails = false;
        SuppressExplanationForThisAction = false;
        PanelState = ExplainPanelState.Explaining;
    }

    /// <summary>Runs the previewed setup operation. False when nothing ran.</summary>
    public async Task<bool> RunSetupAsync(CancellationToken ct = default)
    {
        if (_setup is null || _folderPath is null || _setupRequest is null || !CanRun) return false;

        var outcome = await _setup.RunAsync(_folderPath, _setupRequest, ct);

        if (outcome.Success)
        {
            Narration = outcome.Narration;
            Error = null;
            PanelState = ExplainPanelState.Explaining;
        }
        else
        {
            Narration = null;
            Error = outcome.Error;
            Blockers = outcome.Blockers.ToArray();
            PanelState = outcome.Error is null
                ? ExplainPanelState.Explaining
                : ExplainPanelState.Error;
        }

        if (SetupCompletedAsync is { } handler) await handler(outcome, ct);
        return outcome.Success;
    }
```

In `Clear()`, add one line beside the other resets:

```csharp
        FileContents = null;
```

Finally, make the confirm button drive whichever path is armed. Replace the `ConfirmCommand` assignment in the constructor:

```csharp
        ConfirmCommand = new AsyncRelayCommand(
            () => _setupRequest is null ? RunAsync() : RunSetupAsync(), () => CanRun);
```

`RunAsync` returns `Task<bool>` and `RunSetupAsync` returns `Task<bool>`, so both satisfy `AsyncRelayCommand`'s `Func<Task>` after the compiler discards the result — if the compiler objects, wrap each side: `async () => { if (_setupRequest is null) await RunAsync(); else await RunSetupAsync(); }`.

- [ ] **Step 4: Show the file in the view**

In `src/GitHelper.App/Views/ExplainPanelView.axaml`, replace the command block:

```xml
        <TextBlock Text="The command" FontWeight="Bold" IsVisible="{Binding HasCommandLine}" />
        <Border Padding="8" CornerRadius="4" Background="#20808080"
                IsVisible="{Binding HasCommandLine}">
          <TextBlock Text="{Binding CommandLine}"
                     FontFamily="Consolas, Cascadia Mono, Courier New, monospace"
                     TextWrapping="Wrap" />
        </Border>

        <!-- Some setup operations write a file rather than running a command. Showing the
             file is the honest equivalent of showing the command. -->
        <TextBlock Text="The file" FontWeight="Bold" IsVisible="{Binding HasFileContents}" />
        <Border Padding="8" CornerRadius="4" Background="#20808080"
                IsVisible="{Binding HasFileContents}">
          <SelectableTextBlock Text="{Binding FileContents}"
                               FontFamily="Consolas, Cascadia Mono, Courier New, monospace"
                               TextWrapping="Wrap" />
        </Border>
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/GitHelper.App.Tests/GitHelper.App.Tests.csproj --filter "ExplainPanelSetupTests|ExplainPanelViewTests|ExplainPanelViewModelTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/GitHelper.App/ViewModels/ExplainPanelViewModel.cs src/GitHelper.App/Views/ExplainPanelView.axaml tests/GitHelper.App.Tests/ExplainPanelSetupTests.cs
git commit -m "feat: let the explain panel present setup operations"
```

---

### Task 7: The startup screen offers to create a repository

**Files:**
- Modify: `src/GitHelper.App/ViewModels/StartupViewModel.cs`
- Modify: `src/GitHelper.App/Views/StartupOverlay.axaml`
- Test: `tests/GitHelper.App.Tests/StartupInitOfferTests.cs`

**Interfaces:**
- Consumes: `FolderInspector`, `FolderState` (Task 1).
- Produces: on `StartupViewModel` — `StartupState.FolderIsNotARepository`, observable `FolderState? PendingFolder`, computed `bool IsOfferingInit`, `string PendingFolderSummary`, and `Func<string, CancellationToken, Task>? InitRequestedAsync`.

**The viewmodel does not run `init` itself.** It raises `InitRequestedAsync` with the folder path; the shell routes that to the explain panel, so the operation is explained and confirmed like everything else.

- [ ] **Step 1: Write the failing test**

Create `tests/GitHelper.App.Tests/StartupInitOfferTests.cs`:

```csharp
using GitHelper.App.ViewModels;
using GitHelper.Core.Git;
using GitHelper.Core.Repo;

namespace GitHelper.App.Tests;

public class StartupInitOfferTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "githelper-offer-" + Guid.NewGuid().ToString("N"));

    public StartupInitOfferTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static StartupViewModel NewStartup()
    {
        var runner = new GitRunner();
        return new StartupViewModel(
            new InMemorySettingsStore(),
            new StubFolderPicker(),
            new RepoStateReader(runner),
            new GitEnvironment(runner),
            new FolderInspector());
    }

    [Fact]
    public async Task ANonRepositoryFolderBecomesAnOfferRatherThanADeadEnd()
    {
        var startup = NewStartup();
        await startup.InitializeAsync();

        await startup.OpenAsync(_dir);

        Assert.True(startup.IsOfferingInit);
        Assert.Equal(StartupState.FolderIsNotARepository, startup.State);
        Assert.NotNull(startup.PendingFolder);
        Assert.Equal(_dir, startup.PendingFolder!.Path);
    }

    [Fact]
    public async Task TheSummaryDistinguishesAnEmptyFolderFromOneWithFiles()
    {
        var startup = NewStartup();
        await startup.InitializeAsync();

        await startup.OpenAsync(_dir);
        Assert.Contains("empty", startup.PendingFolderSummary, StringComparison.OrdinalIgnoreCase);

        File.WriteAllText(Path.Combine(_dir, "a.txt"), "x");
        File.WriteAllText(Path.Combine(_dir, "b.txt"), "x");
        await startup.OpenAsync(_dir);
        Assert.Contains("2", startup.PendingFolderSummary);
    }

    [Fact]
    public async Task AcceptingTheOfferRaisesInitRequestedWithTheFolder()
    {
        var startup = NewStartup();
        await startup.InitializeAsync();
        await startup.OpenAsync(_dir);
        string? requested = null;
        startup.InitRequestedAsync = (path, _) => { requested = path; return Task.CompletedTask; };

        await startup.StartTrackingCommand.ExecuteAsync(null);

        Assert.Equal(_dir, requested);
    }

    [Fact]
    public async Task TheOfferIsNotAddedToRecentProjects()
    {
        // It is not a project yet. Recording it would offer a dead entry on the next launch.
        var startup = NewStartup();
        await startup.InitializeAsync();

        await startup.OpenAsync(_dir);

        Assert.Empty(startup.Recents);
    }

    [Fact]
    public async Task OpeningARealRepositoryStillWorks()
    {
        using var repo = await TestRepo.CreateAsync();
        var startup = NewStartup();
        await startup.InitializeAsync();
        string? opened = null;
        startup.RepositoryOpenedAsync = (path, _) => { opened = path; return Task.CompletedTask; };

        await startup.OpenAsync(repo.Path);

        Assert.False(startup.IsOfferingInit);
        Assert.NotNull(opened);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/GitHelper.App.Tests/GitHelper.App.Tests.csproj --filter StartupInitOfferTests`
Expected: FAIL — the constructor takes four arguments and `IsOfferingInit` does not exist.

- [ ] **Step 3: Extend `StartupViewModel`**

In `src/GitHelper.App/ViewModels/StartupViewModel.cs`, add `using GitHelper.Core.Model;`.

Add the enum member:

```csharp
public enum StartupState
{
    /// <summary>Running the environment check.</summary>
    Checking,

    /// <summary>Showing the recents list and the Browse button.</summary>
    AwaitingChoice,

    /// <summary>Git is not installed — nothing else in the app can work.</summary>
    GitMissing,

    /// <summary>A folder was chosen that is not a repository yet; offering to create one.</summary>
    FolderIsNotARepository,
}
```

Add the inspector to the constructor:

```csharp
    private readonly FolderInspector _inspector;

    public StartupViewModel(
        ISettingsStore settings,
        IFolderPicker picker,
        RepoStateReader reader,
        GitEnvironment environment,
        FolderInspector inspector)
    {
        _settings = settings;
        _picker = picker;
        _reader = reader;
        _environment = environment;
        _inspector = inspector;

        BrowseCommand = new AsyncRelayCommand(BrowseAsync);
        SaveIdentityCommand = new AsyncRelayCommand(SaveIdentityAsync, () => CanSaveIdentity);
        StartTrackingCommand = new AsyncRelayCommand(StartTrackingAsync, () => PendingFolder is not null);
    }
```

Add members:

```csharp
    [ObservableProperty] private FolderState? _pendingFolder;

    public IAsyncRelayCommand StartTrackingCommand { get; }

    /// <summary>Raised when the user accepts the offer. The shell routes it to the explain panel.</summary>
    public Func<string, CancellationToken, Task>? InitRequestedAsync { get; set; }

    public bool IsOfferingInit => State == StartupState.FolderIsNotARepository;

    /// <summary>
    /// An empty folder and a folder full of work are the same command but different
    /// situations, and a beginner needs to be told which one they are in.
    /// </summary>
    public string PendingFolderSummary => PendingFolder switch
    {
        null => string.Empty,
        { FileCount: 0 } => "This folder is empty. That is fine — you can start tracking now "
                            + "and add files later.",
        { FileCount: 1 } => "I found 1 file here. Tracking lets you save versions of it.",
        var folder => $"I found {folder.FileCount} files here. "
                      + "Tracking lets you save versions of them.",
    };

    private Task StartTrackingAsync()
        => PendingFolder is { } folder && InitRequestedAsync is { } handler
            ? handler(folder.Path, CancellationToken.None)
            : Task.CompletedTask;
```

Replace the `root is null` branch of `OpenAsync`:

```csharp
        var root = await _reader.FindRepoRootAsync(path, ct);
        if (root is null)
        {
            // Not an error any more: this is where a project starts. The folder is deliberately
            // not added to recents, because it is not a project yet.
            PendingFolder = _inspector.Inspect(path);
            State = StartupState.FolderIsNotARepository;
            return;
        }
```

Add the change hooks beside the existing ones:

```csharp
    partial void OnPendingFolderChanged(FolderState? value)
    {
        OnPropertyChanged(nameof(PendingFolderSummary));
        StartTrackingCommand.NotifyCanExecuteChanged();
    }
```

Extend the existing `OnStateChanged` with one more line:

```csharp
        OnPropertyChanged(nameof(IsOfferingInit));
```

- [ ] **Step 4: Show the offer**

In `src/GitHelper.App/Views/StartupOverlay.axaml`, add this block immediately after the `IsGitMissing` `StackPanel`:

```xml
      <!-- The folder is not a repository yet. Formerly a dead-end error; now the entry point. -->
      <StackPanel Spacing="10" IsVisible="{Binding IsOfferingInit}">
        <TextBlock Text="Not a git project yet" FontWeight="Bold" />
        <TextBlock Text="{Binding PendingFolderSummary}" TextWrapping="Wrap" />
        <TextBlock TextWrapping="Wrap" Opacity="0.8"
                   Text="Git keeps its history in a hidden .git folder, and there is not one here yet." />
        <Button Content="Start tracking this folder"
                Command="{Binding StartTrackingCommand}"
                HorizontalAlignment="Left" />
        <Button Content="Choose a different folder"
                Command="{Binding BrowseCommand}"
                HorizontalAlignment="Left" />
      </StackPanel>
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/GitHelper.App.Tests/GitHelper.App.Tests.csproj --filter "StartupInitOfferTests|StartupViewModelTests|StartupViewModelFlagTests"`
Expected: PASS. Existing `StartupViewModel` tests will fail to compile until their construction sites pass a `FolderInspector`; add `new FolderInspector()` as the fifth argument in each.

- [ ] **Step 6: Commit**

```bash
git add src/GitHelper.App/ViewModels/StartupViewModel.cs src/GitHelper.App/Views/StartupOverlay.axaml tests/GitHelper.App.Tests
git commit -m "feat: offer to create a repository instead of refusing the folder"
```

---

### Task 8: The `.gitignore` banner on the Changes tab

**Files:**
- Modify: `src/GitHelper.App/ViewModels/ChangesViewModel.cs`
- Modify: `src/GitHelper.App/Views/ChangesView.axaml`
- Test: `tests/GitHelper.App.Tests/ChangesGitignoreBannerTests.cs`

**Interfaces:**
- Consumes: `FolderState` (Task 1), `ExplainPanelViewModel.ShowSetupAsync` (Task 6).
- Produces: on `ChangesViewModel` — `void Update(RepoState state, FolderState? folder)`, observable `bool HasGitignoreOffer`, `IAsyncRelayCommand CreateGitignoreCommand`.

**The existing single-argument `Update` is replaced, not overloaded.** Two overloads would let a caller silently pick the one that never populates the banner.

- [ ] **Step 1: Write the failing test**

Create `tests/GitHelper.App.Tests/ChangesGitignoreBannerTests.cs`:

```csharp
using GitHelper.App.ViewModels;
using GitHelper.Core.Actions;
using GitHelper.Core.Content;
using GitHelper.Core.Git;
using GitHelper.Core.Model;
using GitHelper.Core.Repo;
using GitHelper.Core.Setup;

namespace GitHelper.App.Tests;

public class ChangesGitignoreBannerTests
{
    private sealed record Fixture(ChangesViewModel Changes, ExplainPanelViewModel Panel);

    private static Fixture NewFixture()
    {
        var runner = new GitRunner();
        var content = ContentLibrary.Load();
        var actions = new ActionService(runner, new RepoStateReader(runner), content);
        var setup = new SetupService(runner, new FolderInspector(), content);
        var panel = new ExplainPanelViewModel(
            actions, new StubConfirmationDialog(), new InMemorySettingsStore(), setup);
        return new Fixture(new ChangesViewModel(panel), panel);
    }

    private static RepoState State(string root = @"C:\r") => new(
        RepoRoot: root, Branch: "main", IsDetached: false, Upstream: null,
        Ahead: 0, Behind: 0, HasCommits: true, HasRemote: false,
        Changes: Array.Empty<FileChange>(),
        RecentCommits: Array.Empty<CommitInfo>(),
        Branches: Array.Empty<BranchInfo>());

    private static FolderState Folder(bool hasGitignore, string root = @"C:\r")
        => new(root, IsRepository: true, FileCount: 3, HasGitignore: hasGitignore, ProjectType.DotNet);

    [Fact]
    public void TheBannerAppearsWhenThereIsNoGitignore()
    {
        var f = NewFixture();

        f.Changes.Update(State(), Folder(hasGitignore: false));

        Assert.True(f.Changes.HasGitignoreOffer);
    }

    [Fact]
    public void TheBannerStaysHiddenWhenAGitignoreExists()
    {
        var f = NewFixture();

        f.Changes.Update(State(), Folder(hasGitignore: true));

        Assert.False(f.Changes.HasGitignoreOffer);
    }

    [Fact]
    public void TheBannerStaysHiddenWithoutFolderInformation()
    {
        var f = NewFixture();

        f.Changes.Update(State(), folder: null);

        Assert.False(f.Changes.HasGitignoreOffer);
    }

    [Fact]
    public async Task TheBannerPreviewsTheGitignoreOperation()
    {
        using var repo = await TestRepo.CreateAsync();
        File.WriteAllText(Path.Combine(repo.Path, "App.csproj"), "<Project />");
        var f = NewFixture();
        var reader = new RepoStateReader(new GitRunner());
        f.Changes.Update(
            await reader.ReadAsync(repo.Path), new FolderInspector().Inspect(repo.Path));

        await f.Changes.CreateGitignoreCommand.ExecuteAsync(null);

        Assert.Equal("Set up a .gitignore", f.Panel.Title);
        Assert.True(f.Panel.HasFileContents);
        // Previewed only: nothing is written until the user confirms.
        Assert.False(File.Exists(Path.Combine(repo.Path, ".gitignore")));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/GitHelper.App.Tests/GitHelper.App.Tests.csproj --filter ChangesGitignoreBannerTests`
Expected: FAIL — `Update` takes one argument; `HasGitignoreOffer` does not exist.

- [ ] **Step 3: Extend `ChangesViewModel`**

In `src/GitHelper.App/ViewModels/ChangesViewModel.cs`, add `using GitHelper.Core.Setup;`, and a field beside `_repoPath`:

```csharp
    private FolderState? _folder;
```

Add to the constructor:

```csharp
        CreateGitignoreCommand = new AsyncRelayCommand(CreateGitignoreAsync);
```

Add members:

```csharp
    [ObservableProperty] private bool _hasGitignoreOffer;

    public IAsyncRelayCommand CreateGitignoreCommand { get; }
```

Change the signature and add the assignment at the end of `Update`:

```csharp
    public void Update(RepoState state, FolderState? folder)
    {
        _repoPath = state.RepoRoot;
        _folder = folder;
```

```csharp
        // Offered only when the folder is known and has none. A repository with a .gitignore
        // already curated by the user is none of the app's business.
        HasGitignoreOffer = folder is { HasGitignore: false };
```

Add the command body beside `CommitAsync`:

```csharp
    private Task CreateGitignoreAsync()
        => _folder is null
            ? Task.CompletedTask
            // Previews only. The user confirms from the panel, like every other operation.
            : _explain.ShowSetupAsync(_folder.Path, new SetupRequest(SetupService.CreateGitignore));
```

- [ ] **Step 4: Add the banner**

In `src/GitHelper.App/Views/ChangesView.axaml`, add this immediately before the unpushed-work `Border` inside the bottom `StackPanel`:

```xml
        <!-- Offered until the project has a .gitignore. Same shape as the send-changes prompt. -->
        <Border IsVisible="{Binding HasGitignoreOffer}"
                Background="#20808080" CornerRadius="4" Padding="8">
          <Grid ColumnDefinitions="*,Auto">
            <StackPanel Spacing="1" VerticalAlignment="Center">
              <TextBlock Text="No .gitignore yet" FontWeight="SemiBold" TextWrapping="Wrap" />
              <TextBlock Opacity="0.7" FontSize="12" TextWrapping="Wrap"
                         Text="Would you like me to help you set one up? It keeps build output and secrets out of your commits." />
            </StackPanel>
            <Button Grid.Column="1" Content="Set one up"
                    Command="{Binding CreateGitignoreCommand}"
                    VerticalAlignment="Center" Margin="8,0,0,0" />
          </Grid>
        </Border>
```

- [ ] **Step 5: Fix the existing callers**

`MainViewModel.RefreshAsync` calls `Changes.Update(state)`. Task 9 supplies the real `FolderState`; for now make it compile by passing `null`:

```csharp
            Changes.Update(state, null);
```

Every `Changes.Update(...)` call in `tests/GitHelper.App.Tests/ChangesViewModelTests.cs` and `tests/GitHelper.App.Tests/ChangesPushPromptTests.cs` and `tests/GitHelper.App.Tests/TabViewTests.cs` needs a second argument of `null`.

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/GitHelper.App.Tests/GitHelper.App.Tests.csproj --filter "ChangesGitignoreBannerTests|ChangesViewModelTests|ChangesPushPromptTests|TabViewTests"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/GitHelper.App/ViewModels/ChangesViewModel.cs src/GitHelper.App/ViewModels/MainViewModel.cs src/GitHelper.App/Views/ChangesView.axaml tests/GitHelper.App.Tests
git commit -m "feat: offer a .gitignore from the Changes tab"
```

---

### Task 9: Wire it into the shell and prove the journey

**Files:**
- Modify: `src/GitHelper.App/ViewModels/MainViewModel.cs`
- Modify: `src/GitHelper.App/App.axaml.cs`
- Test: `tests/GitHelper.App.Tests/LocalSetupJourneyTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1–8.
- Produces: `MainViewModel` constructor gains a `FolderInspector` parameter; the composition root builds `FolderInspector` and `SetupService`.

**The shell owns the hand-off.** `StartupViewModel` asks for `init`; `MainViewModel` routes it to the panel, and when the panel reports success, opens the folder as a repository — so the user lands in the normal three-pane view with history already working.

- [ ] **Step 1: Write the failing test**

Create `tests/GitHelper.App.Tests/LocalSetupJourneyTests.cs`:

```csharp
using GitHelper.App.Infrastructure;
using GitHelper.App.ViewModels;
using GitHelper.Core.Actions;
using GitHelper.Core.Content;
using GitHelper.Core.Git;
using GitHelper.Core.Repo;
using GitHelper.Core.Setup;

namespace GitHelper.App.Tests;

public class LocalSetupJourneyTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "githelper-journey-" + Guid.NewGuid().ToString("N"));

    public LocalSetupJourneyTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static MainViewModel NewMain()
    {
        var log = new CommandLog();
        var runner = new LoggingGitRunner(new GitRunner(), log);
        var reader = new RepoStateReader(runner);
        var content = ContentLibrary.Load();
        var actions = new ActionService(runner, reader, content);
        var inspector = new FolderInspector();
        var setup = new SetupService(runner, inspector, content);
        var settings = new InMemorySettingsStore();
        var dispatcher = new StubDispatcher();
        var explain = new ExplainPanelViewModel(
            actions, new StubConfirmationDialog(), settings, setup);

        return new MainViewModel(
            reader,
            new StartupViewModel(settings, new StubFolderPicker(), reader,
                new GitEnvironment(runner), inspector),
            explain,
            new CommandLogViewModel(log, dispatcher),
            new ChangesViewModel(explain),
            new HistoryViewModel(explain),
            new BranchesViewModel(explain),
            new RepoWatcher(TimeSpan.FromMilliseconds(50), () => { }),
            new ThemeController(),
            settings,
            dispatcher,
            inspector);
    }

    [Fact]
    public async Task AFolderBecomesATrackedProjectWithAFirstCommit()
    {
        File.WriteAllText(Path.Combine(_dir, "App.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(_dir, "Program.cs"), "// hi");
        using var main = NewMain();

        // 1. Choosing the folder offers to create a repository.
        await main.Startup.OpenAsync(_dir);
        Assert.True(main.Startup.IsOfferingInit);
        Assert.False(main.IsRepositoryOpen);

        // 2. Accepting previews init, then confirming runs it and opens the project.
        await main.Startup.StartTrackingCommand.ExecuteAsync(null);
        Assert.Equal("Start tracking this folder", main.Explain.Title);
        await main.Explain.RunSetupAsync();

        Assert.True(main.IsRepositoryOpen);
        Assert.True(Directory.Exists(Path.Combine(_dir, ".git")));

        // 3. The .gitignore banner is offered, and writing it uses the .NET template.
        Assert.True(main.Changes.HasGitignoreOffer);
        await main.Changes.CreateGitignoreCommand.ExecuteAsync(null);
        await main.Explain.RunSetupAsync();
        Assert.Contains("obj/", await File.ReadAllTextAsync(Path.Combine(_dir, ".gitignore")));

        // 4. Staging and committing work as they do in any other project.
        await main.RefreshAsync();
        Assert.False(main.Changes.HasGitignoreOffer);
        await main.Changes.StageAllCommand.ExecuteAsync(null);
        main.Changes.CommitMessage = "first commit";
        await main.Changes.CommitCommand.ExecuteAsync(null);
        await main.Explain.RunAsync();

        Assert.True(main.History.HasCommits);
        Assert.Equal("first commit", main.History.Commits.Single().Subject);
    }
}
```

This test needs a git identity. `git init` does not set one, so configure it on the new repository between steps 2 and 3:

```csharp
        await new GitRunner().RunAsync(_dir, new[] { "config", "user.name", "Test User" });
        await new GitRunner().RunAsync(_dir, new[] { "config", "user.email", "test@example.com" });
        await new GitRunner().RunAsync(_dir, new[] { "config", "commit.gpgsign", "false" });
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/GitHelper.App.Tests/GitHelper.App.Tests.csproj --filter LocalSetupJourneyTests`
Expected: FAIL — `MainViewModel` has no eleventh parameter.

- [ ] **Step 3: Wire the shell**

In `src/GitHelper.App/ViewModels/MainViewModel.cs`, add `using GitHelper.Core.Model;` and `using GitHelper.Core.Setup;`, then add the parameter and field:

```csharp
    private readonly FolderInspector _inspector;
```

```csharp
        ISettingsStore settings,
        IUiDispatcher dispatcher,
        FolderInspector inspector)
    {
```

```csharp
        _inspector = inspector;
```

Add the two hand-offs beside the existing subscriptions in the constructor:

```csharp
        Startup.InitRequestedAsync = (folderPath, ct) =>
            Explain.ShowSetupAsync(folderPath, new SetupRequest(SetupService.InitRepository), ct);
        Explain.SetupCompletedAsync = OnSetupCompletedAsync;
```

Add the handler beside `OnActionCompletedAsync`:

```csharp
    /// <summary>
    /// A folder that has just become a repository is opened straight away, so the user lands
    /// in the normal view rather than being sent back to the startup screen to find it again.
    /// </summary>
    private async Task OnSetupCompletedAsync(SetupOutcome outcome, CancellationToken ct)
    {
        if (outcome.Narration is { Length: > 0 }) StatusMessage = outcome.Narration;

        if (!outcome.Success) return;

        if (_repoPath is null)
        {
            var folder = Startup.PendingFolder;
            if (folder is not null) await OpenRepositoryAsync(folder.Path, ct);
            return;
        }

        await RefreshAsync(ct);
    }
```

In `Dispose`, add one line beside the existing unsubscribes:

```csharp
        Startup.InitRequestedAsync = null;
        Explain.SetupCompletedAsync = null;
```

In `RefreshAsync`, replace the `Changes.Update` call so the banner gets real data:

```csharp
            Changes.Update(state, _inspector.Inspect(state.RepoRoot));
```

- [ ] **Step 4: Update the composition root**

In `src/GitHelper.App/App.axaml.cs`, add `using GitHelper.Core.Setup;`, then inside `BuildMainViewModel`:

```csharp
        var inspector = new FolderInspector();
        var setupService = new SetupService(runner, inspector, content);
```

```csharp
        var explain = new ExplainPanelViewModel(actions, confirmations, settings, setupService);
        var startup = new StartupViewModel(settings, picker, reader, environment, inspector);
```

and add `inspector` as the final constructor argument to `new MainViewModel(...)`.

- [ ] **Step 5: Run the journey test**

Run: `dotnet test tests/GitHelper.App.Tests/GitHelper.App.Tests.csproj --filter LocalSetupJourneyTests`
Expected: PASS, 1 test.

- [ ] **Step 6: Run the whole suite**

Run: `dotnet test`
Expected: PASS. Every `new MainViewModel(...)` in the test suite needs `new FolderInspector()` as the final argument — `MainViewModelTests`, `ShellTests`, and `EmptyRepositoryTests` all build one.

Note: `MainViewModelTests.RunningAnActionSurfacesItsNarrationAsAStatusMessage` and its neighbours fail intermittently under full-suite parallel load (roughly one run in six, never in isolation). That is the pre-existing `StubDispatcher` race recorded in the progress ledger, not a regression from this work. Re-run to confirm before investigating.

- [ ] **Step 7: Launch the app and walk it**

```bash
dotnet run --project src/GitHelper.App/GitHelper.App.csproj
```

Create a scratch folder with a couple of files, choose it, and confirm:

- the startup card says the folder is not a git project and names the file count
- **Start tracking this folder** previews `git init -b main` with all four headings
- confirming runs it, the scrim clears, and the top bar shows the folder name and `main`
- the Changes tab shows a **No .gitignore yet** banner
- **Set one up** shows the template under "The file", not "The command"
- confirming writes `.gitignore` and the banner disappears
- the command log shows `git init -b main`

Anything wrong here is a real finding — record what you saw rather than what was expected.

- [ ] **Step 8: Commit**

```bash
git add src/GitHelper.App tests/GitHelper.App.Tests
git commit -m "feat: wire folder setup into the shell"
```

---

## Done

A folder that is not a repository is now where a project starts rather than where the app gives
up. The user is offered `git init`, then a `.gitignore` chosen for what the folder looks like,
each explained in the same four headings as every other operation and each confirmed before it
runs.

The sibling plan — putting the result on GitHub — builds on this without changing any of it.
