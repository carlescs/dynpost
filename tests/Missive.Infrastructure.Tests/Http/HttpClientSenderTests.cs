using System.Net;
using System.Net.Sockets;
using System.Text;
using Missive.Application.Http;
using Missive.Domain.Http;
using Missive.Infrastructure.Http;

namespace Missive.Infrastructure.Tests.Http;

public sealed class HttpClientSenderTests
{
    private static readonly Uri Url = new("https://api.example.com/users?page=2");

    /// <summary>Records the request it receives and answers with a fixed response.</summary>
    private sealed class RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return await respond(request, cancellationToken);
        }
    }

    private static RecordingHandler Respond(HttpStatusCode status = HttpStatusCode.OK, string body = "", string contentType = "application/json") =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, contentType),
        }));

    private static OutgoingRequest Request(
        string method = "GET",
        IReadOnlyList<KeyValuePair<string, string>>? headers = null,
        OutgoingBody? body = null) =>
        new(method, Url, headers ?? [], body);

    [Fact]
    public async Task SendAsync_SendsMethodUrlAndHeaders()
    {
        var handler = Respond();
        using var sender = new HttpClientSender(new HttpClient(handler));

        await sender.SendAsync(Request(headers: [KeyValuePair.Create("Accept", "application/json"), KeyValuePair.Create("X-Trace", "1")]));

        Assert.Equal(HttpMethod.Get, handler.Request!.Method);
        Assert.Equal(Url, handler.Request.RequestUri);
        Assert.Equal("application/json", handler.Request.Headers.Accept.Single().MediaType);
        Assert.Equal(["1"], handler.Request.Headers.GetValues("X-Trace"));
        Assert.Equal([HttpClientSender.DefaultUserAgent], handler.Request.Headers.GetValues("User-Agent"));
        Assert.Null(handler.Body);
    }

    [Fact]
    public async Task SendAsync_KeepsAUserAgentSetByTheRequest()
    {
        var handler = Respond();
        using var sender = new HttpClientSender(new HttpClient(handler));

        await sender.SendAsync(Request(headers: [KeyValuePair.Create("user-agent", "curl/8")]));

        Assert.Equal(["curl/8"], handler.Request!.Headers.GetValues("User-Agent"));
    }

    [Fact]
    public async Task SendAsync_SendsTextBodiesWithTheirDefaultOrExplicitContentType()
    {
        var handler = Respond();
        using var sender = new HttpClientSender(new HttpClient(handler));

        await sender.SendAsync(Request("POST", body: new TextBody("""{"a":1}""", "application/json")));
        Assert.Equal("""{"a":1}""", handler.Body);
        Assert.Equal("application/json", handler.Request!.Content!.Headers.ContentType!.MediaType);

        await sender.SendAsync(Request("POST", [KeyValuePair.Create("Content-Type", "application/vnd.api+json")], new TextBody("{}", "application/json")));
        Assert.Equal(["application/vnd.api+json"], handler.Request!.Content!.Headers.GetValues("Content-Type"));
    }

    [Fact]
    public async Task SendAsync_SendsMultipartFields()
    {
        var handler = Respond();
        using var sender = new HttpClientSender(new HttpClient(handler));

        await sender.SendAsync(Request("POST", [KeyValuePair.Create("Content-Type", "text/plain")], new MultipartBody([KeyValuePair.Create("name", "Ada")])));

        Assert.Equal("multipart/form-data", handler.Request!.Content!.Headers.ContentType!.MediaType);
        Assert.Contains("name=name", handler.Body);
        Assert.Contains("Ada", handler.Body);
    }

    [Fact]
    public async Task SendAsync_ReturnsStatusHeadersAndBody()
    {
        var handler = new RecordingHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("""{"error":"nope"}""", Encoding.UTF8, "application/json") };
            response.Headers.Add("X-Custom", "abc");
            return Task.FromResult(response);
        });
        using var sender = new HttpClientSender(new HttpClient(handler));

        var result = await sender.SendAsync(Request());

        Assert.True(result.IsSuccess, result.Error?.Message);
        var response = result.Value;
        Assert.Equal(404, response.StatusCode);
        Assert.Equal("Not Found", response.ReasonPhrase);
        Assert.Equal("""{"error":"nope"}""", Encoding.UTF8.GetString(response.Body));
        Assert.False(response.Truncated);
        Assert.Contains(KeyValuePair.Create("X-Custom", "abc"), response.Headers);
        Assert.Contains(KeyValuePair.Create("Content-Type", "application/json; charset=utf-8"), response.Headers);
    }

    [Fact]
    public async Task SendAsync_CutsOffLargeBodies()
    {
        using var sender = new HttpClientSender(new HttpClient(Respond(body: new string('x', 100))), maxBodyBytes: 10);

        var response = (await sender.SendAsync(Request())).Value;

        Assert.True(response.Truncated);
        Assert.Equal(10, response.Body.Length);
    }

    [Fact]
    public async Task SendAsync_ReportsTimeouts()
    {
        var handler = new RecordingHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage();
        });
        using var sender = new HttpClientSender(new HttpClient(handler), timeout: TimeSpan.FromMilliseconds(50));

        var result = await sender.SendAsync(Request());

        Assert.Equal(HttpErrors.TimeoutCode, result.Error?.Code);
    }

    [Fact]
    public async Task SendAsync_ThrowsWhenCancelled()
    {
        var handler = new RecordingHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage();
        });
        using var sender = new HttpClientSender(new HttpClient(handler));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sender.SendAsync(Request(), cancellation.Token));
    }

    [Fact]
    public async Task SendAsync_ReportsUnreachableHostsOverARealConnection()
    {
        // Find a free port and close it again, so nothing listens there.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        using var sender = new HttpClientSender();
        var result = await sender.SendAsync(new OutgoingRequest("GET", new Uri($"http://127.0.0.1:{port}/"), [], null));

        Assert.Equal(HttpErrors.NetworkErrorCode, result.Error?.Code);
        Assert.Contains($"127.0.0.1:{port}", result.Error!.Message);
    }
}
