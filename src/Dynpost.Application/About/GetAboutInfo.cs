namespace Dynpost.Application.About;

/// <summary>
/// Reads what the About page shows: the app version, the platform it runs on and where to find
/// the project.
/// </summary>
public sealed class GetAboutInfo(IAppInfo info)
{
    public AboutDto Execute() => new(
        info.Version, info.Runtime, info.OperatingSystem, AboutLinks.Homepage, AboutLinks.Repository);
}
