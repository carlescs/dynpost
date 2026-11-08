using System.Runtime.CompilerServices;

namespace Missive.Infrastructure.Tests.Git;

/// <summary>Points git at a known global config so the tests don't depend on the developer's own settings.</summary>
internal static class GitTestEnvironment
{
    [ModuleInitializer]
    internal static void Isolate()
    {
        var config = Path.Combine(Path.GetTempPath(), $"missive-tests-gitconfig-{Environment.ProcessId}");
        File.WriteAllText(
            config,
            "[safe]\n\tbareRepository = all\n[user]\n\tuseConfigOnly = true\n[commit]\n\tgpgsign = false\n[init]\n\tdefaultBranch = main\n");
        Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", config);
        Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", "1");

        // Identity and config overrides from the environment would beat the repositories' own settings.
        foreach (var name in new[] { "GIT_AUTHOR_NAME", "GIT_AUTHOR_EMAIL", "GIT_COMMITTER_NAME", "GIT_COMMITTER_EMAIL", "GIT_CONFIG_COUNT", "GIT_CONFIG_PARAMETERS" })
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }
}
