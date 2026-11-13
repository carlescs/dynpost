using Dynpost.Application.Common;
using Dynpost.Application.Cookies;
using Dynpost.Application.Workspaces;
using Dynpost.Domain.Http;
using NSubstitute;

namespace Dynpost.Application.Tests;

using Jars = IReadOnlyDictionary<string, IReadOnlyList<StoredCookie>>;

public class CookieUseCaseTests
{
    private const string Root = "/work/my-api";
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly ICookieJarStore _store = Substitute.For<ICookieJarStore>();
    private readonly CurrentWorkspace _current = new();
    private readonly TimeProvider _time = Substitute.For<TimeProvider>();

    private static readonly StoredCookie Session = new("session", "abc", "api.example.com", "/");
    private static readonly StoredCookie Theme = new("theme", "dark", ".example.com", "/", Now.AddDays(1));
    private static readonly StoredCookie Expired = new("old", "x", "api.example.com", "/", Now.AddSeconds(-1));

    public CookieUseCaseTests()
    {
        _current.Open(Root);
        _time.GetUtcNow().Returns(Now);
        _store.LoadAsync(Root, Arg.Any<CancellationToken>()).Returns(Result<Jars>.Success(
            new Dictionary<string, IReadOnlyList<StoredCookie>>
            {
                ["dev.yaml"] = [Theme, Expired, Session],
                ["prod.yaml"] = [Session],
            }));
        _store.SaveAsync(Root, Arg.Any<Jars>(), Arg.Any<CancellationToken>()).Returns(Unit.Value);
    }

    [Fact]
    public async Task List_SortsByDomainAndLeavesOutExpiredCookies()
    {
        var cookies = await new ListCookies(_store, _current, _time).ExecuteAsync("dev.yaml");

        Assert.Equal(["session", "theme"], cookies.Value.Select(c => c.Name));
    }

    [Fact]
    public async Task List_AnInvalidEnvironment_IsRefused()
    {
        var cookies = await new ListCookies(_store, _current, _time).ExecuteAsync("../dev.yaml");

        Assert.Equal(ErrorCodes.InvalidPath, cookies.Error?.Code);
    }

    [Fact]
    public async Task Delete_RemovesOnlyThatCookie()
    {
        var cookies = await new DeleteCookie(_store, _current, _time).ExecuteAsync("dev.yaml", "session", "API.example.com", "/");

        Assert.Equal(["theme"], cookies.Value.Select(c => c.Name));
        await _store.Received(1).SaveAsync(
            Root,
            Arg.Is<Jars>(j => j["dev.yaml"].SequenceEqual(new[] { Theme }) && j["prod.yaml"].SequenceEqual(new[] { Session })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Clear_RemovesTheEnvironmentsJar()
    {
        var result = await new ClearCookies(_store, _current).ExecuteAsync("dev.yaml");

        Assert.True(result.IsSuccess);
        await _store.Received(1).SaveAsync(Root, Arg.Is<Jars>(j => j.Count == 1 && j.ContainsKey("prod.yaml")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UseCases_NeedAWorkspace()
    {
        var closed = new CurrentWorkspace();

        Assert.Equal(ErrorCodes.NoWorkspace, (await new ListCookies(_store, closed, _time).ExecuteAsync("dev.yaml")).Error?.Code);
        Assert.Equal(ErrorCodes.NoWorkspace, (await new ClearCookies(_store, closed).ExecuteAsync("dev.yaml")).Error?.Code);
    }
}
