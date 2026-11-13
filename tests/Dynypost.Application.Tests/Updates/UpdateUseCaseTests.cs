using Dynypost.Application.Common;
using Dynypost.Application.Updates;
using NSubstitute;

namespace Dynypost.Application.Tests.Updates;

public class UpdateUseCaseTests
{
    private readonly IAppUpdater _updater = Substitute.For<IAppUpdater>();

    public UpdateUseCaseTests()
    {
        _updater.IsSupported.Returns(true);
        _updater.CurrentVersion.Returns("1.0.0");
    }

    [Fact]
    public async Task Check_WhenThisCopyCannotUpdate_ReportsUnsupportedWithoutLooking()
    {
        _updater.IsSupported.Returns(false);

        var result = await new CheckForUpdate(_updater).ExecuteAsync();

        Assert.Equal(new UpdateStatusDto(UpdateStatusDto.Unsupported, "1.0.0"), result.Value);
        await _updater.DidNotReceiveWithAnyArgs().FindUpdateAsync(default);
    }

    [Fact]
    public async Task Check_WithoutANewerVersion_IsUpToDate()
    {
        _updater.FindUpdateAsync(Arg.Any<CancellationToken>()).Returns(Result<string?>.Success(null));

        var result = await new CheckForUpdate(_updater).ExecuteAsync();

        Assert.Equal(new UpdateStatusDto(UpdateStatusDto.UpToDate, "1.0.0"), result.Value);
        await _updater.DidNotReceiveWithAnyArgs().DownloadUpdateAsync(default);
    }

    [Fact]
    public async Task Check_WithANewerVersion_DownloadsItButDoesNotInstallIt()
    {
        _updater.FindUpdateAsync(Arg.Any<CancellationToken>()).Returns(Result<string?>.Success("1.1.0"));
        _updater.DownloadUpdateAsync(Arg.Any<CancellationToken>()).Returns(Result<Unit>.Success(Unit.Value));

        var result = await new CheckForUpdate(_updater).ExecuteAsync();

        Assert.Equal(new UpdateStatusDto(UpdateStatusDto.Ready, "1.0.0", "1.1.0"), result.Value);
        _updater.DidNotReceive().RestartToUpdate();
    }

    [Fact]
    public async Task Check_WhenLookingFails_ReturnsTheError()
    {
        var error = new Error(ErrorCodes.UpdateFailed, "offline");
        _updater.FindUpdateAsync(Arg.Any<CancellationToken>()).Returns(Result<string?>.Failure(error));

        var result = await new CheckForUpdate(_updater).ExecuteAsync();

        Assert.Equal(error, result.Error);
    }

    [Fact]
    public async Task Check_WhenTheDownloadFails_ReturnsTheError()
    {
        var error = new Error(ErrorCodes.UpdateFailed, "disk full");
        _updater.FindUpdateAsync(Arg.Any<CancellationToken>()).Returns(Result<string?>.Success("1.1.0"));
        _updater.DownloadUpdateAsync(Arg.Any<CancellationToken>()).Returns(Result<Unit>.Failure(error));

        var result = await new CheckForUpdate(_updater).ExecuteAsync();

        Assert.Equal(error, result.Error);
    }

    [Fact]
    public void Restart_InstallsTheDownloadedUpdate()
    {
        _updater.RestartToUpdate().Returns(Result<Unit>.Success(Unit.Value));

        var result = new RestartToUpdate(_updater).Execute();

        Assert.True(result.IsSuccess);
        _updater.Received(1).RestartToUpdate();
    }

    [Fact]
    public void Restart_WhenThisCopyCannotUpdate_Fails()
    {
        _updater.IsSupported.Returns(false);

        var result = new RestartToUpdate(_updater).Execute();

        Assert.Equal(ErrorCodes.UpdateFailed, result.Error?.Code);
        _updater.DidNotReceive().RestartToUpdate();
    }
}
