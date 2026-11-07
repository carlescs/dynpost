using Missive.Application.Common;
using Missive.Application.Http;
using Missive.Application.Interchange;
using NSubstitute;

namespace Missive.Application.Tests.Http;

public class SaveResponseBodyTests
{
    private readonly ResponseBodies _bodies = new();
    private readonly IExternalFiles _files = Substitute.For<IExternalFiles>();

    public SaveResponseBodyTests()
    {
        _files.WriteBytesAsync(default!, default!, default).ReturnsForAnyArgs(Unit.Value);
    }

    [Fact]
    public async Task Execute_WritesTheBytesAsReceived()
    {
        byte[] content = [1, 2, 3, 0, 255];
        _bodies.Add("send-1", new StoredBody(content, "image.png"));
        var saver = new SaveResponseBody(_bodies, _files);

        Assert.Equal("image.png", saver.SuggestedFileName("send-1").Value);
        var result = await saver.ExecuteAsync("send-1", "/home/me/picture.png");

        Assert.Equal("/home/me/picture.png", result.Value);
        await _files.Received(1).WriteBytesAsync("/home/me/picture.png", content, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_AddsTheSuggestedExtensionWhenTheNameHasNone()
    {
        _bodies.Add("send-1", new StoredBody([1], "report.pdf"));

        var result = await new SaveResponseBody(_bodies, _files).ExecuteAsync("send-1", "/home/me/my report");

        Assert.Equal("/home/me/my report.pdf", result.Value);
    }

    [Fact]
    public async Task Execute_AnUnknownResponse_IsNotFound()
    {
        var saver = new SaveResponseBody(_bodies, _files);

        Assert.Equal(ErrorCodes.NotFound, saver.SuggestedFileName("send-9").Error?.Code);
        Assert.Equal(ErrorCodes.NotFound, (await saver.ExecuteAsync("send-9", "/tmp/x")).Error?.Code);
        await _files.DidNotReceiveWithAnyArgs().WriteBytesAsync(default!, default!, default);
    }

    [Fact]
    public void Bodies_DropTheOldestBeyondTheLimitButKeepTheLatest()
    {
        var big = new byte[ResponseBodies.MaxBytes / 2 + 1];
        _bodies.Add("a", new StoredBody(big, "a"));
        _bodies.Add("b", new StoredBody(big, "b"));
        Assert.Null(_bodies.Find("a"));
        Assert.NotNull(_bodies.Find("b"));

        _bodies.Add("c", new StoredBody(new byte[ResponseBodies.MaxBytes + 1], "c"));
        Assert.Null(_bodies.Find("b"));
        Assert.NotNull(_bodies.Find("c"));

        for (var i = 0; i <= ResponseBodies.MaxCount; i++)
        {
            _bodies.Add($"small-{i}", new StoredBody([1], "s"));
        }

        Assert.Null(_bodies.Find("small-0"));
        Assert.NotNull(_bodies.Find($"small-{ResponseBodies.MaxCount}"));
    }
}
