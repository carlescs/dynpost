using System.Net;
using System.Net.Sockets;
using System.Text;
using Missive.Application.Common;
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

        await sender.SendAsync(Request("POST", [KeyValuePair.Create("Content-Type", "text/plain")], new MultipartBody([new MultipartField("name", "Ada")])));

        Assert.Equal("multipart/form-data", handler.Request!.Content!.Headers.ContentType!.MediaType);
        Assert.Contains("name=name", handler.Body);
        Assert.Contains("Ada", handler.Body);
    }

    [Fact]
    public async Task SendAsync_SendsBinaryAndMultipartFileContent()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png");
        await File.WriteAllBytesAsync(path, [65, 66]);
        try
        {
            var handler = Respond();
            using var sender = new HttpClientSender(new HttpClient(handler));

            var binary = await sender.SendAsync(Request("POST", body: new BinaryBody(path)));
            Assert.True(binary.IsSuccess, binary.Error?.Message);
            Assert.Equal("AB", handler.Body);
            Assert.Equal("application/octet-stream", handler.Request!.Content!.Headers.ContentType!.MediaType);

            await sender.SendAsync(Request("POST", body: new MultipartBody([
                new MultipartField("avatar", FilePath: path, ContentType: "image/png"),
            ])));
            Assert.Contains("filename=", handler.Body);
            Assert.Contains("Content-Type: image/png", handler.Body);
            Assert.Contains("AB", handler.Body);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SendAsync_ReportsMissingBodyFiles()
    {
        using var sender = new HttpClientSender(new HttpClient(Respond()));

        var result = await sender.SendAsync(Request("POST", body: new BinaryBody(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")))));

        Assert.Equal(ErrorCodes.Io, result.Error?.Code);
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

    /// <summary>Answers each request in turn with the next response, recording what it received.</summary>
    private sealed class SequenceHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string? Body)> Received { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Received.Add((request, body));
            return responses[Math.Min(Received.Count - 1, responses.Length - 1)](request);
        }
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> Redirect(HttpStatusCode status, string location, string? setCookie = null) => _ =>
    {
        var response = new HttpResponseMessage(status);
        response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        if (setCookie is not null)
        {
            response.Headers.Add("Set-Cookie", setCookie);
        }

        return response;
    };

    private static Func<HttpRequestMessage, HttpResponseMessage> Ok(string body = "done", string? setCookie = null) => _ =>
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        if (setCookie is not null)
        {
            response.Headers.Add("Set-Cookie", setCookie);
        }

        return response;
    };

    [Fact]
    public async Task SendAsync_FollowsRedirectsAndReportsEachHop()
    {
        var handler = new SequenceHandler(Redirect(HttpStatusCode.Found, "/next"), Redirect(HttpStatusCode.MovedPermanently, "https://other.example.com/final"), Ok());
        using var sender = new HttpClientSender(new HttpClient(handler));

        var result = await sender.SendAsync(Request("POST", [KeyValuePair.Create("Authorization", "Bearer abc"), KeyValuePair.Create("X-Keep", "1")], new TextBody("{}", "application/json")));

        Assert.True(result.IsSuccess, result.Error?.Message);
        var response = result.Value;
        Assert.Equal(200, response.StatusCode);
        Assert.Equal("done", Encoding.UTF8.GetString(response.Body));
        Assert.Equal(
            [(302, Url.AbsoluteUri, "https://api.example.com/next"), (301, "https://api.example.com/next", "https://other.example.com/final")],
            response.Redirects.Select(hop => (hop.StatusCode, hop.Url.AbsoluteUri, hop.Location.AbsoluteUri)));
        Assert.Equal(["POST", "GET", "GET"], handler.Received.Select(r => r.Request.Method.Method));
        Assert.Equal("{}", handler.Received[0].Body);
        Assert.Null(handler.Received[1].Body);
        Assert.False(handler.Received[1].Request.Headers.Contains("Authorization"));
        Assert.Contains(KeyValuePair.Create("Authorization", "Bearer abc"), response.Redirects[0].SentHeaders);
        Assert.DoesNotContain(response.Redirects[1].SentHeaders, header => header.Key == "Authorization");
        Assert.True(handler.Received[2].Request.Headers.Contains("X-Keep"));
        Assert.Equal("GET", response.Sent!.Method);
        Assert.Equal("https://other.example.com/final", response.Sent.Url.AbsoluteUri);
        Assert.Contains(KeyValuePair.Create("User-Agent", HttpClientSender.DefaultUserAgent), response.Sent.Headers);
        Assert.Null(response.Timings!.Dns);
        Assert.Null(response.Cookies);
    }

    [Theory]
    [InlineData(HttpStatusCode.TemporaryRedirect)]
    [InlineData(HttpStatusCode.PermanentRedirect)]
    public async Task SendAsync_KeepsTheMethodAndBodyFor307And308(HttpStatusCode status)
    {
        var handler = new SequenceHandler(Redirect(status, "/again"), Ok());
        using var sender = new HttpClientSender(new HttpClient(handler));

        var response = (await sender.SendAsync(Request("PUT", body: new TextBody("payload", "text/plain")))).Value;

        Assert.Equal(["PUT", "PUT"], handler.Received.Select(r => r.Request.Method.Method));
        Assert.Equal("payload", handler.Received[1].Body);
        Assert.Contains(KeyValuePair.Create("Content-Length", "7"), response.Sent!.Headers);
    }

    [Fact]
    public async Task SendAsync_DoesNotFollowRedirectsFromHttpsToHttp()
    {
        var handler = new SequenceHandler(Redirect(HttpStatusCode.Found, "http://api.example.com/insecure"), Ok());
        using var sender = new HttpClientSender(new HttpClient(handler));

        var response = (await sender.SendAsync(Request())).Value;

        Assert.Equal(302, response.StatusCode);
        Assert.Empty(response.Redirects);
        Assert.Single(handler.Received);
    }

    [Fact]
    public async Task SendAsync_StopsFollowingRedirectsAfterTheLimit()
    {
        var handler = new SequenceHandler(Redirect(HttpStatusCode.Found, "/loop"));
        using var sender = new HttpClientSender(new HttpClient(handler));

        var response = (await sender.SendAsync(Request())).Value;

        Assert.Equal(302, response.StatusCode);
        Assert.Equal(HttpClientSender.MaxRedirects, response.Redirects.Count);
        Assert.Equal(HttpClientSender.MaxRedirects + 1, handler.Received.Count);
    }

    [Fact]
    public async Task SendAsync_WithACookieJar_SendsAndKeepsCookiesAcrossRedirects()
    {
        var handler = new SequenceHandler(
            Redirect(HttpStatusCode.Found, "/home", "session=abc; Path=/; HttpOnly"),
            Ok(setCookie: "theme=dark; Max-Age=3600"));
        using var sender = new HttpClientSender(new HttpClient(handler));
        var request = Request(headers: [KeyValuePair.Create("Cookie", "own=1")]) with
        {
            Cookies = [new StoredCookie("stored", "old", "api.example.com", "/"), new StoredCookie("elsewhere", "x", "other.test", "/")],
        };

        var response = (await sender.SendAsync(request)).Value;

        Assert.Equal(["own=1; stored=old"], handler.Received[0].Request.Headers.GetValues("Cookie"));
        var second = Assert.Single(handler.Received[1].Request.Headers.GetValues("Cookie"));
        Assert.StartsWith("own=1; ", second);
        Assert.Contains("session=abc", second);
        Assert.Contains("stored=old", second);
        Assert.DoesNotContain("elsewhere", second);
        Assert.Equal(["elsewhere", "session", "stored", "theme"], response.Cookies!.Select(c => c.Name).Order());
        var session = response.Cookies!.Single(c => c.Name == "session");
        Assert.True(session.HttpOnly);
        Assert.Null(session.Expires);
        Assert.NotNull(response.Cookies!.Single(c => c.Name == "theme").Expires);
    }

    [Fact]
    public async Task SendAsync_KeepsHostOnlyCookiesHostOnlyAcrossSaves()
    {
        var api = new Uri("https://api.example.com/login");
        var handler = new SequenceHandler(Ok(setCookie: "host=1; Path=/"), Ok(), Ok(), Ok(setCookie: "dom=2; Domain=api.example.com; Path=/"), Ok(), Ok());
        using var sender = new HttpClientSender(new HttpClient(handler));

        var first = (await sender.SendAsync(new OutgoingRequest("GET", api, [], null) { Cookies = [] })).Value.Cookies!;
        Assert.Equal("api.example.com", Assert.Single(first).Domain);

        // A later send restores the jar: the host still gets the cookie, a subdomain doesn't.
        await sender.SendAsync(new OutgoingRequest("GET", api, [], null) { Cookies = first });
        await sender.SendAsync(new OutgoingRequest("GET", new Uri("https://child.api.example.com/"), [], null) { Cookies = first });
        Assert.Equal(["host=1"], handler.Received[1].Request.Headers.GetValues("Cookie"));
        Assert.False(handler.Received[2].Request.Headers.Contains("Cookie"));

        // A cookie with a Domain attribute is stored with a leading dot and reaches subdomains.
        var both = (await sender.SendAsync(new OutgoingRequest("GET", api, [], null) { Cookies = first })).Value.Cookies!;
        Assert.Equal(".api.example.com", both.Single(c => c.Name == "dom").Domain);
        await sender.SendAsync(new OutgoingRequest("GET", new Uri("https://child.api.example.com/"), [], null) { Cookies = both });
        Assert.Equal(["dom=2"], handler.Received[4].Request.Headers.GetValues("Cookie"));
    }

    [Fact]
    public async Task SendAsync_WithoutACookieJar_DoesNotKeepCookies()
    {
        var handler = new SequenceHandler(Redirect(HttpStatusCode.Found, "/home", "session=abc"), Ok());
        using var sender = new HttpClientSender(new HttpClient(handler));

        var response = (await sender.SendAsync(Request())).Value;

        Assert.False(handler.Received[1].Request.Headers.Contains("Cookie"));
        Assert.Null(response.Cookies);
        Assert.Contains(KeyValuePair.Create("Set-Cookie", "session=abc"), response.Redirects[0].Headers);
    }

    [Fact]
    public async Task SendAsync_MeasuresConnectionPhasesOverARealConnection()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serving = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            var buffer = new byte[4096];
            var received = new StringBuilder();
            while (!received.ToString().Contains("\r\n\r\n"))
            {
                var read = await stream.ReadAsync(buffer);
                if (read == 0)
                {
                    break;
                }

                received.Append(Encoding.ASCII.GetString(buffer, 0, read));
            }

            var reply = "HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: 5\r\nConnection: close\r\n\r\nhello";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(reply));
            return received.ToString();
        });

        try
        {
            using var sender = new HttpClientSender();
            var result = await sender.SendAsync(new OutgoingRequest("GET", new Uri($"http://127.0.0.1:{port}/hi"), [], null));

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal("hello", Encoding.UTF8.GetString(result.Value.Body));
            var timings = result.Value.Timings!;
            Assert.NotNull(timings.Dns);
            Assert.NotNull(timings.Connect);
            Assert.Null(timings.Tls);
            Assert.True(timings.Wait >= TimeSpan.Zero);
            Assert.True(timings.Download >= TimeSpan.Zero);
            Assert.StartsWith("GET /hi HTTP/1.1", await serving);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Theory]
    [InlineData("https://api.example.com/users", "api.example.com")]
    [InlineData("http://api.example.com:8080/users", "api.example.com:8080")]
    [InlineData("https://bücher.example/", "xn--bcher-kva.example")]
    [InlineData("http://[::1]:5000/", "[::1]:5000")]
    public void SentHeaders_StartsWithTheHostTheHandlerWrites(string url, string host)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, url);
        message.Headers.TryAddWithoutValidation("Accept", "*/*");

        Assert.Equal(
            [KeyValuePair.Create("Host", host), KeyValuePair.Create("Accept", "*/*")],
            HttpClientSender.SentHeaders(message));
    }

    [Fact]
    public void SentHeaders_KeepsAHostSetByTheRequest()
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, "https://127.0.0.1/");
        message.Headers.Host = "api.example.com";

        Assert.Equal([KeyValuePair.Create("Host", "api.example.com")], HttpClientSender.SentHeaders(message));
    }

    [Fact]
    public async Task SendAsync_ReportsEveryHeaderSentOverARealConnection()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serving = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            var buffer = new byte[4096];
            var received = new StringBuilder();
            while (!received.ToString().Contains("\r\n\r\n"))
            {
                var read = await stream.ReadAsync(buffer);
                if (read == 0)
                {
                    break;
                }

                received.Append(Encoding.ASCII.GetString(buffer, 0, read));
            }

            var reply = "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(reply));
            return received.ToString();
        });

        try
        {
            using var sender = new HttpClientSender();
            var result = await sender.SendAsync(new OutgoingRequest(
                "POST",
                new Uri($"http://127.0.0.1:{port}/items"),
                [KeyValuePair.Create("X-Trace", "abc")],
                new TextBody("{}", "application/json")));

            Assert.True(result.IsSuccess, result.Error?.Message);
            var wire = (await serving)
                .Split("\r\n\r\n")[0]
                .Split("\r\n")
                .Skip(1)
                .Select(line => line.Split(": ", 2))
                .Select(parts => (Name: parts[0].ToLowerInvariant(), Value: parts[1]))
                .ToList();
            var reported = result.Value.Sent!.Headers
                .Select(header => (Name: header.Key.ToLowerInvariant(), header.Value))
                .ToList();
            Assert.Equal(wire.OrderBy(h => h.Name), reported.OrderBy(h => h.Name));
            Assert.Equal(("host", $"127.0.0.1:{port}"), reported[0]);
            Assert.Contains(reported, header => header.Name == "accept-encoding");
        }
        finally
        {
            listener.Stop();
        }
    }
}
