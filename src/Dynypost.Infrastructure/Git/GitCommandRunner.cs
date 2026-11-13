using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Dynypost.Infrastructure.Git;

internal sealed record GitCommandResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}

/// <summary>Thrown when the git executable cannot be started.</summary>
internal sealed class GitNotFoundException(string executable, Exception inner)
    : Exception($"Could not start '{executable}'.", inner);

/// <summary>
/// Runs the git command-line client, so Dynypost uses the user's own git configuration, hooks and
/// credential helpers.
/// </summary>
internal sealed class GitCommandRunner(string executable)
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public async Task<GitCommandResult> RunAsync(
        string workingDirectory,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        // There is no terminal to answer prompts: fail instead of waiting forever. Credential
        // helpers (including GUI ones such as Git Credential Manager) still work.
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        // Never open an editor for merge commit messages.
        startInfo.Environment["GIT_MERGE_AUTOEDIT"] = "no";
        // Don't take locks that are only an optimization (such as the index refresh of a status
        // read), so background refreshes never block the user's other git tools, nor they Dynypost.
        startInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        // Untranslated messages, so error output can be recognized.
        startInfo.Environment["LC_ALL"] = "C";

        foreach (var (name, value) in environment ?? new Dictionary<string, string>())
        {
            startInfo.Environment[name] = value;
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new GitNotFoundException(executable, ex);
        }

        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // The process already exited.
            }

            throw;
        }

        return new GitCommandResult(process.ExitCode, await output, await error);
    }
}
