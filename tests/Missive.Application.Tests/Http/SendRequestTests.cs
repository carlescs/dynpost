using System.Text;
using Missive.Application.Common;
using Missive.Application.Http;
using Missive.Application.Requests;
using Missive.Domain.Http;
using NSubstitute;

namespace Missive.Application.Tests.Http;

public class SendRequestTests
{
    private readonly IHttpSender _sender = Substitute.For<IHttpSender>();
    private readonly InFlightRequests _inFlight = new();

    private SendRequest CreateSendRequest() => new(_sender, _inFlight);

    private static RequestDto Request(string url = "https://api.example.com/users", string name = "List users") =>
        new(name, "GET", url, [new KeyValueDto("page", "2")], [], new AuthDto("bearer", Token: "abc"), new BodyDto("none", ""));

    private static HttpResponseData Response(string body, string? contentType = "application/json; charset=utf-8", int status = 200) =>
        new(
            status,
            "OK",
            contentType is null ? [] : [KeyValuePair.Create("Content-Type", contentType)],
            Encoding.UTF8.GetBytes(body),
            false,
            TimeSpan.FromMilliseconds(123.4));

    [Fact]
    public async Task Execute_SendsThePreparedRequestAndReturnsTheResponse()
    {
        _sender.SendAsync(Arg.Any<OutgoingRequest>(), Arg.Any<CancellationToken>()).Returns(Response("""{"ok":true}"""));

        var result = await CreateSendRequest().ExecuteAsync("1", Request());

        Assert.True(result.IsSuccess, result.Error?.Message);
        var response = result.Value;
        Assert.Equal(200, response.Status);
        Assert.Equal("OK", response.StatusText);
        Assert.Equal("application/json", response.ContentType);
        Assert.Equal("""{"ok":true}""", response.Body);
        Assert.Equal("text", response.BodyEncoding);
        Assert.Equal(11, response.Size);
        Assert.Equal(123, response.DurationMs);
        Assert.Equal(new KeyValueDto("Content-Type", "application/json; charset=utf-8"), Assert.Single(response.Headers));
        await _sender.Received(1).SendAsync(
            Arg.Is<OutgoingRequest>(r =>
                r.Method == "GET"
                && r.Url.AbsoluteUri == "https://api.example.com/users?page=2"
                && r.Headers.Contains(KeyValuePair.Create("Authorization", "Bearer abc"))),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_SendsUnnamedRequests()
    {
        _sender.SendAsync(Arg.Any<OutgoingRequest>(), Arg.Any<CancellationToken>()).Returns(Response(""));

        var result = await CreateSendRequest().ExecuteAsync("1", Request(name: " "));

        Assert.True(result.IsSuccess, result.Error?.Message);
    }

    [Fact]
    public async Task Execute_ReportsRequestsThatCantBeSent()
    {
        var result = await CreateSendRequest().ExecuteAsync("1", Request(url: "{{baseUrl}}/users"));

        Assert.Equal(ErrorCodes.InvalidRequest, result.Error?.Code);
        Assert.Contains("baseUrl", result.Error!.Message);
        await _sender.DidNotReceiveWithAnyArgs().SendAsync(default!, default);
    }

    [Fact]
    public async Task Execute_PassesNetworkErrorsThrough()
    {
        _sender.SendAsync(Arg.Any<OutgoingRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<HttpResponseData>.Failure(HttpErrors.NetworkError("api.example.com", "Connection refused")));

        var result = await CreateSendRequest().ExecuteAsync("1", Request());

        Assert.Equal(HttpErrors.NetworkErrorCode, result.Error?.Code);
    }

    [Fact]
    public async Task Execute_ReturnsBinaryBodiesAsBase64()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x00 };
        _sender.SendAsync(Arg.Any<OutgoingRequest>(), Arg.Any<CancellationToken>())
            .Returns(new HttpResponseData(200, "OK", [KeyValuePair.Create("Content-Type", "image/png")], bytes, false, TimeSpan.Zero));

        var response = (await CreateSendRequest().ExecuteAsync("1", Request())).Value;

        Assert.Equal("base64", response.BodyEncoding);
        Assert.Equal(Convert.ToBase64String(bytes), response.Body);
        Assert.Equal(5, response.Size);
    }

    [Theory]
    [InlineData("plain words", null, "text")]
    [InlineData("<p>x</p>", "text/html", "text")]
    [InlineData("{}", "application/problem+json", "text")]
    [InlineData("abc", "application/octet-stream", "base64")]
    public async Task Execute_DetectsTextBodies(string body, string? contentType, string expectedEncoding)
    {
        _sender.SendAsync(Arg.Any<OutgoingRequest>(), Arg.Any<CancellationToken>()).Returns(Response(body, contentType));

        var response = (await CreateSendRequest().ExecuteAsync("1", Request())).Value;

        Assert.Equal(expectedEncoding, response.BodyEncoding);
    }

    [Fact]
    public async Task Execute_DecodesTheDeclaredCharset()
    {
        var latin1 = Encoding.Latin1.GetBytes("café");
        _sender.SendAsync(Arg.Any<OutgoingRequest>(), Arg.Any<CancellationToken>())
            .Returns(new HttpResponseData(200, "OK", [KeyValuePair.Create("Content-Type", "text/plain; charset=ISO-8859-1")], latin1, false, TimeSpan.Zero));

        var response = (await CreateSendRequest().ExecuteAsync("1", Request())).Value;

        Assert.Equal("café", response.Body);
    }

    [Fact]
    public async Task Cancel_StopsTheRequestWithTheSameId()
    {
        var started = new TaskCompletionSource();
        _sender.SendAsync(Arg.Any<OutgoingRequest>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>());
            return Result<HttpResponseData>.Success(Response(""));
        });

        var sending = CreateSendRequest().ExecuteAsync("send-1", Request());
        await started.Task;

        Assert.False(new CancelRequest(_inFlight).Execute("other"));
        Assert.True(new CancelRequest(_inFlight).Execute("send-1"));
        var result = await sending;

        Assert.Equal(HttpErrors.CancelledCode, result.Error?.Code);
        Assert.False(new CancelRequest(_inFlight).Execute("send-1"));
    }
}
