# Running GitHelper

## From source

```bash
dotnet run --project src/GitHelper.App/GitHelper.App.csproj
```

Requires the .NET 10 SDK and git on `PATH`.

## Building the standalone executable

```bash
dotnet publish src/GitHelper.App/GitHelper.App.csproj -c Release -o publish
```

This produces `publish/GitHelper.App.exe` — a single self-contained file with the .NET
runtime bundled, so it runs on a Windows machine with no SDK installed. Git itself is not
bundled: the app drives the real `git` executable, and tells you plainly if it is missing.

The executable is around 125 MB. Most of that is the bundled runtime plus the
ReadyToRun images, which trade size for a faster cold start.

## Running the tests

```bash
dotnet test
```

The suite is headless. Viewmodel tests drive real git against throwaway repositories in
the temp directory; view tests render through Avalonia's headless platform, so no window
appears and the suite runs in CI unchanged.
