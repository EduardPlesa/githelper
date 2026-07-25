using System.Diagnostics;
using System.Text;

namespace GitHelper.Core.Git;

public sealed class GitRunner : IGitRunner
{
    /// <summary>
    /// Prepended to every invocation but never shown to the user.
    /// core.quotepath=false stops git mangling non-ASCII filenames into octal escapes.
    /// </summary>
    private static readonly string[] InternalArgs = { "-c", "core.quotepath=false" };

    public async Task<GitCommandResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> args,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        // ArgumentList, never Arguments: the OS receives an argv array, so quoting
        // and injection defects cannot occur regardless of what a path contains.
        foreach (var a in InternalArgs) psi.ArgumentList.Add(a);
        foreach (var a in args) psi.ArgumentList.Add(a);

        // git must never block waiting on a prompt the user cannot see.
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";

        var stopwatch = Stopwatch.StartNew();
        using var process = new Process { StartInfo = psi };
        process.Start();

        // Both streams must be drained concurrently and before waiting for exit.
        // Reading one to completion first deadlocks as soon as the other fills its buffer.
        var stdOutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stdErrTask = process.StandardError.ReadToEndAsync(ct);

        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        var stdOut = await stdOutTask.ConfigureAwait(false);
        var stdErr = await stdErrTask.ConfigureAwait(false);
        stopwatch.Stop();

        return new GitCommandResult(
            args.ToArray(),
            stdOut,
            stdErr,
            process.ExitCode,
            stopwatch.Elapsed);
    }
}
