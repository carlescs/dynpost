using System.Diagnostics;

namespace Dynpost.Infrastructure.Tests.Git;

/// <summary>Runs git directly to arrange and inspect test repositories.</summary>
internal static class TestGit
{
    public static string Run(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";

        using var process = Process.Start(startInfo)!;
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} failed ({process.ExitCode}): {error.Result}");
        }

        return output;
    }

    /// <summary>Gives a repository an identity and settings that make tests independent of the machine.</summary>
    public static void Configure(string repository, string name = "Test")
    {
        Run(repository, "config", "user.name", name);
        Run(repository, "config", "user.email", $"{name.ToLowerInvariant()}@example.com");
        Run(repository, "config", "commit.gpgsign", "false");
        Run(repository, "config", "core.autocrlf", "false");
    }

    public static void CommitAll(string repository, string message)
    {
        Run(repository, "add", "--all");
        Run(repository, "commit", "--quiet", "--message", message);
    }

    public static string Head(string repository) => Run(repository, "rev-parse", "HEAD").Trim();
}
