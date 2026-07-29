# GitHelper

**A desktop git client for people who don't know git yet.**

Every action explains what it will do — in plain English — *before* it runs, shows the exact
`git` command it is about to execute, warns about consequences in concrete terms, and tells you
how to undo it afterwards.

It works on your **real repositories**, not a simulator — and it will start one for you if the
folder isn't a repository yet. You learn on your own work, with guardrails.

![The explain panel: staging a file](docs/images/explain-panel.png)

---

## Why this exists

Every git GUI assumes you already understand git. They give you buttons labelled `fetch`,
`rebase`, and `HEAD~1`, and quietly assume you know what those mean and what they'll cost you.

Beginners don't learn git from those tools. They learn a sequence of clicks that seems to work,
and then panic the first time it doesn't.

GitHelper takes the opposite approach: it explains at **the moment of decision**, which is when
an explanation actually sticks. And it has a deliberate second goal — that you eventually stop
needing it. The real command is always on screen, and every command the app runs accumulates in
a log strip you can copy and paste into a terminal.

The app is meant to make itself obsolete.

## How it works

Every action follows the same three beats:

### 1. Explain — nothing has run yet

The panel always shows four headings, always in the same order, so the structure becomes
predictable instead of something you have to re-read:

| Heading | What it gives you |
|---|---|
| **What this does** | Plain English. Jargon isn't avoided — it's underlined, with the definition on hover, so you still learn the vocabulary. |
| **The command** | The exact `git` invocation, copyable. |
| **What could go wrong** | Concrete consequences, not generic warnings. |
| **How to undo this** | Every action, including the ones that can't be undone — which say so. |

### 2. Confirm — gated by how dangerous the action actually is

| Danger | Gate |
|---|---|
| **Safe** | Runs immediately, explanation shown alongside. |
| **Caution** | Inline confirmation button. |
| **Destructive** | A **modal dialog** — deliberately in a different screen position and weight, so it can't be dismissed by the muscle memory you build clicking inline buttons. Its consequence sentence names the real file. |

There's a per-action **"stop explaining this one"** toggle, because a tool that nags gets
reflex-clicked past, which defeats the whole point. Destructive confirmations are never
suppressible.

### 3. Narrate — what actually happened

The app snapshots repository state before and after, then reports the **observed** difference
rather than the intended one:

> Created commit `a1b2c3d` with 3 files. `main` is now 1 commit ahead of `origin/main`.

This matters: narrating what actually happened means the app can't confidently claim success
when git did something unexpected.

## Features

- **Starts the repository for you** — pick a folder that isn't one yet and the app offers `git init`, explained like everything else, instead of dead-ending on an error.
- **Offers a `.gitignore`** chosen for the kind of project it finds, shown in full and commented line by line before it's written. It will never overwrite one you already have.
- **Publishes to GitHub** — creates the connection, explains that git and GitHub are different things, and warns about the empty-repository trap before it bites. It never asks for a token: you create the repository, the app wires it up.
- **Three-pane shell** — file/history/branch lists, the explain panel, and a permanent command log.
- **A command log that teaches the CLI** — every `git` invocation the app makes, with exit codes, in pasteable form. Multi-word arguments come out correctly quoted.
- **Plain-English error translation** — with the raw git output always one click away, never hidden.
- **Live refresh** — edit a file outside the app and the Changes tab updates within about a second, debounced so a burst of writes causes one refresh, not fifty.
- **A glossary built into the prose** — hover any underlined term for its definition.
- **Recent projects, light/dark/system theme**, and your per-action preferences, all persisted.
- **Identity setup** — if `user.name` / `user.email` aren't configured, the app offers to set them, rather than letting your first commit fail with a wall of git configuration advice.
- **Refuses to pretend** — if git isn't installed, it says so plainly instead of failing mysteriously later.

## Starting a project

Most git GUIs begin at "open an existing repository", which is not where a project begins.
GitHelper starts one step earlier.

Pick a folder that isn't a repository and it says so, tells you what it found there, and offers
to fix it — in the same panel, with the same four headings, as every other operation:

> **Not a git project yet**
> I found 2 files here. Tracking lets you save versions of them.
> Git keeps its history in a hidden `.git` folder, and there is not one here yet.
> `[ Start tracking this folder ]`  `[ Choose a different folder ]`

Once the repository exists, the Changes tab offers a `.gitignore` picked from what the project
looks like — .NET, Node, Python, Java, or a general-purpose fallback. Five short templates, every
rule commented, because the app has to be able to *explain* the file rather than just drop it in.

![Setting up a .gitignore: the panel shows the file, not a command](docs/images/gitignore-setup.png)

These two are **setup operations**, and they are deliberately not in the action table below:

| | Why it isn't an ordinary action |
|---|---|
| **Start tracking this folder** | Runs before a repository exists, so there is no repository state to describe it against |
| **Set up a .gitignore** | **Isn't a git command at all** — it writes a file |

That second one is why the panel's third heading switches from **The command** to **The file**,
showing the exact contents that are about to be written. No fake command is invented to fill the
slot, and no heading is quietly dropped.

**Your `.gitignore` is never overwritten.** If one already exists the offer doesn't appear, and
the write itself uses an atomic create-new — so even a file that appears in the half-second
between the check and the write is safe. Losing a file you curated isn't undoable by anything the
app could show you.

## Publishing to GitHub

Most beginners are never told that git and GitHub are two different things. The Changes tab
says so at the moment it matters — whenever a project has no online copy, even before the
first commit:

> **Not on GitHub yet**
> This project only exists on this computer. Create an empty repository on GitHub — no
> README, no .gitignore — then paste its address here.
> `[ Create on GitHub ]`  `[ Connect ]`

The empty-repository warning is not decoration. A repository created with "Add a README"
ticked already has a commit of its own, and the first push is then rejected for reasons the
raw git message ("non-fast-forward") gives a beginner no way to decode. Both the risk and
the recovery are spelled out — before, in the explanation, and after, in the translated
error.

**The app stops at the address.** Creating the repository for you would need a personal
access token, and this app has no field to type one into, by design. Signing in for the
first push is git's own credential helper, which may open a browser window — the push
explanation says so in advance, so it arrives expected rather than alarming.

## The action set

Fifteen actions, covering roughly the 90% of beginner git that doesn't involve conflicts.

| Action | Command | Danger |
|---|---|---|
| Stage file | `git add -- <path>` | Safe |
| Unstage file | `git restore --staged -- <path>` | Safe |
| Stage all | `git add -A` | Safe |
| Unstage all | `git restore --staged -- .` | Safe |
| Commit | `git commit -m <message>` | Caution |
| Create branch | `git switch -c <name>` | Safe |
| Switch branch | `git switch <name>` | Caution |
| Check for updates | `git fetch` | Safe |
| Get changes | `git pull --ff-only` | Caution |
| Send changes | `git push` | Caution |
| Discard file | `git restore -- <path>` | **Destructive** |
| Undo last commit | `git reset --soft HEAD~1` | Caution |
| Delete branch | `git branch -d <name>` | Caution |
| Connect to GitHub | `git remote add origin <url>` | Caution |
| Disconnect from GitHub | `git remote remove origin` | Caution |

Four choices in that table are teaching decisions, not technical ones:

- **`pull --ff-only`, never a plain `pull`.** A beginner should never get a merge commit they
  didn't ask for and can't explain. When it can't fast-forward, it refuses — and the refusal is
  explained.
- **`branch -d`, never `-D`.** The safe form refuses to delete a branch holding unmerged work.
  That refusal gets explained rather than overridden.
- **Creating a branch is Safe; switching to one is Caution.** Even though `switch -c` does both.
  Creating at the current HEAD carries your changes along and can't lose anything; switching to
  an *existing* branch can collide with uncommitted work. The danger levels reflect that real
  difference.
- **The app connects; it never creates the GitHub repository.** Creating one through the API
  needs a personal access token, and no view in this app may contain a token field. The
  trade is a click on github.com in exchange for a promise the app can keep absolutely.

`discard-file` is the only Destructive action. That's the point: there are very few ways to lose
work.

## Architecture

```
GitHelper.Core      Git runner, parsers, action descriptors, safety rules,
                    error translation, content loader.  No Avalonia reference.
GitHelper.Content   Authored explanations and glossary, embedded as resources.
GitHelper.App       Avalonia UI. Views + viewmodels, MVVM via CommunityToolkit source generators.
```

`Core` having no UI dependency is the load-bearing constraint: the entire engine is testable
headlessly, and the UI could be replaced without touching any git or teaching logic.

**Actions are data, not code paths.** Each is a descriptor — id, argv builder, preconditions,
danger level, explanation id. Adding one later is a descriptor plus a content file: no new UI
code, no new branches in the flow.

**Explanations are hand-written content files, not generated at runtime.** Deterministic,
offline, no API key, no per-click latency or cost — and reviewable for correctness, which matters
a great deal when the thing you're doing is teaching.

**Setup operations get their own small service, on purpose.** The action pipeline reads a
`RepoState` before it can describe anything — and before `git init` there is no repository to
read. Rather than make `RepoState` nullable across all twelve preconditions and every consumer,
a deliberately small `SetupService` sits beside it over a `FolderState`: what can be known about
a folder *without* git. Exactly two operations live there, and one panel drives both services, so
the user never sees the seam.

### Some details worth calling out

- **Every git call goes through one choke point**, using `ProcessStartInfo.ArgumentList` — an argv
  array, never a joined string, never a shell. Quoting and injection defects can't occur.
- **stdout and stderr are read concurrently.** Reading them sequentially deadlocks once output
  exceeds a pipe buffer — the classic .NET failure here, designed out from the start.
- **`-c core.quotepath=false`** on every invocation, so non-ASCII filenames aren't mangled into
  octal escapes.
- **Status is parsed from `--porcelain=v2 -z`** with a record reader, not line splitting — the
  `-z` output is NUL-separated and rename records carry two paths in one record.
- **Git runs off the UI thread.** A slow `push` never freezes the window.

## Testing

**415 tests**, all headless.

Viewmodel and engine tests drive **real git** against throwaway repositories in the temp
directory — not mocks — so the parsers are tested against the git that's actually installed. View
tests render through Avalonia's headless platform, so no window appears and the suite runs
unchanged in CI.

```bash
dotnet test
```

## Getting started

**Requires** the .NET 10 SDK and `git` on your `PATH`.

```bash
dotnet run --project src/GitHelper.App/GitHelper.App.csproj
```

### Building a standalone executable

```bash
dotnet publish src/GitHelper.App/GitHelper.App.csproj -c Release -o publish
```

Produces a single self-contained `publish/GitHelper.App.exe` (~125 MB) with the .NET runtime
bundled — it runs on a Windows machine with no SDK installed. Git itself is not bundled: the app
drives the real `git` binary, and tells you plainly if it's missing.

## Deliberately not in v1

Recorded so they read as decisions rather than oversights. The reasoning, the cost of each, and
the order they should close in is in **[the roadmap](docs/roadmap.md)** — including two items that
are *declined* rather than deferred:

- **Merge, rebase, stash, cherry-pick, and tags.**
- **Guided conflict resolution** — this is milestone 2. It's the scariest part of git for a
  beginner and the most complex UI in the product; it deserves its own design pass rather than
  being squeezed in.
- **A diff viewer** — the Changes tab lists *what* changed, not the contents of the change.
- ~~Remote management.~~ Connecting to and disconnecting from GitHub shipped — see
  [Publishing to GitHub](#publishing-to-github). A view for multiple or renamed remotes is
  still deferred.
- **Hunk-level staging — declined.** It needs a constructed patch on stdin, and `GitRunner` takes
  argv arrays only, never a constructed string. That single constraint is why quoting and
  injection defects can't occur here. It's also an expert affordance in a tool premised on the
  absence of expertise.
- **Submodules — declined.** They confuse experts. A tool for people who don't know git has no
  business shipping them.

## Project layout

```
src/
  GitHelper.Core/       engine — git, parsing, actions, errors
  GitHelper.Content/    authored explanations + glossary + .gitignore templates
                        (15 actions, 2 setup ops, 11 terms, 5 templates)
  GitHelper.App/        Avalonia UI
tests/
  GitHelper.Core.Tests/ engine tests against real git
  GitHelper.App.Tests/  viewmodel + headless view tests
  GitHelper.TestSupport/ throwaway-repo helpers
docs/
  running-githelper.md  build and run instructions
```

## Built with

.NET 10 · [Avalonia](https://avaloniaui.net/) 11.3 · [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/) 8.4 · xUnit
