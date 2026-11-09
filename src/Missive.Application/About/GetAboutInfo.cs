namespace Missive.Application.About;

/// <summary>Reads what the About page shows: the app version and the platform it runs on.</summary>
public sealed class GetAboutInfo(IAppInfo info)
{
    public AboutDto Execute() => new(info.Version, info.Runtime, info.OperatingSystem);
}
