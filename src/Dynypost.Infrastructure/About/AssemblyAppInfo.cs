using System.Reflection;
using System.Runtime.InteropServices;
using Dynypost.Application.About;

namespace Dynypost.Infrastructure.About;

/// <summary>Reads the app version from the entry assembly.</summary>
public sealed class AssemblyAppInfo : IAppInfo
{
    public string Version { get; } = ReadVersion();

    public string Runtime => RuntimeInformation.FrameworkDescription;

    public string OperatingSystem => RuntimeInformation.OSDescription;

    private static string ReadVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(AssemblyAppInfo).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
        // Drop the source revision SDK builds append ("1.2.3+abcdef").
        var plus = version.IndexOf('+');
        return plus < 0 ? version : version[..plus];
    }
}
