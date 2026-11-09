namespace Missive.Application.About;

/// <summary>Describes the running app.</summary>
public interface IAppInfo
{
    /// <summary>The running version, without build metadata.</summary>
    string Version { get; }

    /// <summary>The .NET runtime the app runs on.</summary>
    string Runtime { get; }

    /// <summary>The operating system the app runs on.</summary>
    string OperatingSystem { get; }
}
