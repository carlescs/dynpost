using Dynpost.Application.Common;
using Dynpost.Infrastructure.Updates;

namespace Dynpost.Infrastructure.Tests.Updates;

/// <summary>The test host is not an installed copy, so it must not try to update itself.</summary>
public class VelopackAppUpdaterTests
{
    private readonly VelopackAppUpdater _updater = new("https://github.com/carlescs/dynpost");

    [Fact]
    public void AnUninstalledCopy_IsNotSupported() => Assert.False(_updater.IsSupported);

    [Fact]
    public async Task LookingForUpdates_FailsWithoutGoingOnline()
    {
        var result = await _updater.FindUpdateAsync();

        Assert.Equal(ErrorCodes.UpdateFailed, result.Error?.Code);
    }

    [Fact]
    public async Task Downloading_BeforeAnUpdateWasFound_Fails()
    {
        var result = await _updater.DownloadUpdateAsync();

        Assert.Equal(ErrorCodes.UpdateFailed, result.Error?.Code);
    }

    [Fact]
    public void Restarting_WithoutADownloadedUpdate_FailsInsteadOfExiting()
    {
        var result = _updater.RestartToUpdate();

        Assert.Equal(ErrorCodes.UpdateFailed, result.Error?.Code);
    }
}
