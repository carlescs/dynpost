using Missive.Application.Common;
using Missive.Application.History;
using Missive.Application.Http;
using Missive.Application.Requests;
using Missive.Application.Settings;
using Missive.Application.Workspaces;
using Missive.Domain.Workspaces;
using NSubstitute;

namespace Missive.Application.Tests.History;

public class ResponseHistoryTests
{
    private const string Root = "/work/my-api";
    private const string Path = "users/get-user.yaml";
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly IHistoryStore _store = Substitute.For<IHistoryStore>();
    private readonly IAppSettingsStore _settings = Substitute.For<IAppSettingsStore>();
    private readonly CurrentWorkspace _current = new();

    public ResponseHistoryTests()
    {
        _current.Open(Root);
        _settings.LoadAsync(Arg.Any<CancellationToken>()).Returns(AppSettings.Default);
        _store.AddAsync(default!, default!, default!, default!).ReturnsForAnyArgs(Unit.Value);
    }

    private static ResponseDto Response(string body = "{}", string encoding = "text") =>
        new(200, "OK", [new KeyValueDto("Content-Type", "application/json")], "application/json", body, encoding, body.Length, false, 42);

    [Fact]
    public void Redaction_HidesCookieValuesAndSecrets()
    {
        var response = Response("""{"key":"s3cret"}""") with
        {
            Headers =
            [
                new KeyValueDto("Set-Cookie", "session=abc123; Path=/; HttpOnly"),
                new KeyValueDto("X-Echo", "token s3cret"),
            ],
            Cookies = [new ResponseCookieDto("session", "abc123", null, "/", null, false, true, null)],
            HistoryId = "x",
        };

        var (kept, omitted) = HistoryRedaction.Prepare(response, ["s3cret"], 1024);

        Assert.False(omitted);
        Assert.Equal("session=••••••; Path=/; HttpOnly", kept.Headers[0].Value);
        Assert.Equal("token ••••••", kept.Headers[1].Value);
        Assert.Equal("••••••", Assert.Single(kept.Cookies!).Value);
        Assert.Equal("""{"key":"••••••"}""", kept.Body);
        Assert.Null(kept.HistoryId);
    }

    [Fact]
    public void Redaction_CutsLargeTextBodiesAndLeavesOutLargeBinaryOnes()
    {
        var (text, textOmitted) = HistoryRedaction.Prepare(Response("héllo world"), [], 2);
        var (binary, binaryOmitted) = HistoryRedaction.Prepare(Response("AAAAAAAA", "base64"), [], 3);
        var (small, smallOmitted) = HistoryRedaction.Prepare(Response("AAAA", "base64"), [], 3);

        Assert.Equal("h", text.Body);
        Assert.True(text.Truncated);
        Assert.False(textOmitted);
        Assert.Equal("", binary.Body);
        Assert.True(binaryOmitted);
        Assert.Equal("AAAA", small.Body);
        Assert.False(smallOmitted);
    }

    [Fact]
    public async Task Record_UsesTheSettingsLimits()
    {
        _settings.LoadAsync(Arg.Any<CancellationToken>())
            .Returns(new AppSettings(History: new HistorySettings(MaxEntries: 5, MaxBodyBytes: 2)));

        var id = await new ResponseHistory(_store, _settings).RecordAsync(Root, Path, "GET", "https://x", Response("abcdef"), [], Now);

        Assert.NotNull(id);
        Assert.True(ResponseHistory.IsValidId(id));
        Assert.StartsWith("20261107T120000000-", id);
        await _store.Received(1).AddAsync(
            Root,
            WorkspacePath.Parse(Path),
            Arg.Is<HistoryEntryDetailsDto>(e => e.Response.Body == "ab" && e.Entry.Size == 6 && e.Entry.DurationMs == 42),
            new HistoryLimits(5, HistorySettings.MaxTotalBytes),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("users/notes.txt")]
    [InlineData("../outside.yaml")]
    public async Task Record_IgnoresRequestsThatArentFiles(string? path)
    {
        var id = await new ResponseHistory(_store, _settings).RecordAsync(Root, path, "GET", "https://x", Response(), [], Now);

        Assert.Null(id);
        await _store.DidNotReceiveWithAnyArgs().AddAsync(default!, default!, default!, default!);
    }

    [Fact]
    public async Task Record_ReturnsNoIdWhenTheEntryCannotBeStored()
    {
        _store.AddAsync(default!, default!, default!, default!).ReturnsForAnyArgs(new Error(ErrorCodes.Io, "disk full"));

        var id = await new ResponseHistory(_store, _settings).RecordAsync(Root, Path, "GET", "https://x", Response(), [], Now);

        Assert.Null(id);
    }

    [Fact]
    public async Task Get_ReportsEntriesThatAreNoLongerKept()
    {
        _store.LoadAsync(Root, WorkspacePath.Parse(Path), "20261107T120000000-aa", Arg.Any<CancellationToken>())
            .Returns(Result<HistoryEntryDetailsDto?>.Success(null));

        var missing = await new GetResponseHistoryEntry(_store, _current).ExecuteAsync(Path, "20261107T120000000-aa");
        var invalid = await new GetResponseHistoryEntry(_store, _current).ExecuteAsync(Path, "../secrets");

        Assert.Equal(ErrorCodes.NotFound, missing.Error?.Code);
        Assert.Equal(ErrorCodes.NotFound, invalid.Error?.Code);
    }

    [Fact]
    public async Task List_RejectsPathsThatArentRequests()
    {
        var result = await new ListResponseHistory(_store, _current).ExecuteAsync("missive.yaml");

        Assert.Equal(ErrorCodes.InvalidPath, result.Error?.Code);
    }

    [Fact]
    public async Task Delete_DeletesAndReturnsTheEntriesLeft()
    {
        IReadOnlyList<HistoryEntryDto> left = [new HistoryEntryDto("b", Now, "GET", "https://x", 200, "OK", 1, 2, null)];
        _store.DeleteAsync(Root, WorkspacePath.Parse(Path), null, Arg.Any<CancellationToken>()).Returns(Unit.Value);
        _store.ListAsync(Root, WorkspacePath.Parse(Path), Arg.Any<CancellationToken>()).Returns(Result<IReadOnlyList<HistoryEntryDto>>.Success(left));

        var result = await new DeleteResponseHistory(_store, _current).ExecuteAsync(Path, null);

        Assert.Same(left, result.Value);
        await _store.Received(1).DeleteAsync(Root, WorkspacePath.Parse(Path), null, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0, 1024)]
    [InlineData(501, 1024)]
    [InlineData(10, -1)]
    [InlineData(10, 60L * 1024 * 1024)]
    public async Task Settings_RejectOutOfRangeLimits(int maxEntries, long maxBodyBytes)
    {
        var result = await new SetHistorySettings(_settings).ExecuteAsync(new HistorySettingsDto(true, maxEntries, maxBodyBytes));

        Assert.Equal(SetHistorySettings.InvalidSettingsCode, result.Error?.Code);
        _ = _settings.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Fact]
    public async Task Settings_AreSavedKeepingTheOthers()
    {
        _settings.UpdateAsync(Arg.Any<Func<AppSettings, AppSettings>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<Result<AppSettings>>(call.Arg<Func<AppSettings, AppSettings>>()(new AppSettings("/work/api", "dark"))));

        var result = await new SetHistorySettings(_settings).ExecuteAsync(new HistorySettingsDto(false, 50, 2048));

        Assert.Equal("dark", result.Value!.Theme);
        Assert.Equal(new HistorySettingsDto(false, 50, 2048), result.Value.History);
    }

    [Fact]
    public void Settings_DefaultToKeepingHistory()
    {
        var settings = AppSettingsDto.From(AppSettings.Default);

        Assert.Equal(new HistorySettingsDto(true, HistorySettings.DefaultMaxEntries, HistorySettings.DefaultMaxBodyBytes), settings.History);
    }
}
