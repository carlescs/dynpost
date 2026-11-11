using Missive.Application.History;
using Missive.Application.Http;
using Missive.Application.Requests;
using Missive.Domain.Workspaces;
using Missive.Infrastructure.History;

namespace Missive.Infrastructure.Tests.History;

public sealed class JsonHistoryStoreTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly HistoryLimits Unlimited = new(100, long.MaxValue);

    private readonly string _root = Directory.CreateTempSubdirectory("missive-tests-").FullName;
    private readonly JsonHistoryStore _store = new();
    private int _count;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string HistoryFolder => Path.Combine(_root, ".missive", JsonHistoryStore.FolderName);

    private static WorkspacePath P(string path) => WorkspacePath.Parse(path);

    private HistoryEntryDetailsDto Entry(string body = "{}", int status = 200)
    {
        var n = ++_count;
        var id = $"20261107T1200000{n:00}-0000000{n % 10}";
        var response = new ResponseDto(status, "OK", [new KeyValueDto("Content-Type", "application/json")], "application/json", body, "text", body.Length, false, 10 + n);
        return new HistoryEntryDetailsDto(
            new HistoryEntryDto(id, Start.AddMinutes(n), "GET", "https://api.example.com/users", status, "OK", 10 + n, body.Length, "application/json"),
            response);
    }

    private async Task<HistoryEntryDetailsDto> AddAsync(string path, HistoryLimits? limits = null, string body = "{}")
    {
        var entry = Entry(body);
        Assert.True((await _store.AddAsync(_root, P(path), entry, limits ?? Unlimited)).IsSuccess);
        return entry;
    }

    private async Task<IReadOnlyList<string>> IdsAsync(string path) =>
        (await _store.ListAsync(_root, P(path))).Value.Select(e => e.Id).ToList();

    [Fact]
    public async Task Entries_RoundTripNewestFirstAndAreGitIgnored()
    {
        var first = await AddAsync("users/get-user.yaml", body: """{"id":1}""");
        var second = await AddAsync("users/get-user.yaml", body: """{"id":2}""");

        var listed = (await _store.ListAsync(_root, P("users/get-user.yaml"))).Value;
        var loaded = (await _store.LoadAsync(_root, P("users/get-user.yaml"), first.Entry.Id)).Value;

        Assert.Equal([second.Entry, first.Entry], listed);
        Assert.Equal(first.Entry, loaded!.Entry);
        Assert.Equal("""{"id":1}""", loaded.Response.Body);
        Assert.Equal(first.Response.Headers, loaded.Response.Headers);
        Assert.Equal(".missive/\n", File.ReadAllText(Path.Combine(_root, ".gitignore")));
        var index = File.ReadAllText(Path.Combine(HistoryFolder, JsonHistoryStore.IndexFileName));
        Assert.EndsWith("]\n}\n", index);
        Assert.DoesNotContain("\r", index);
        Assert.Empty(await IdsAsync("users/other.yaml"));
    }

    [Fact]
    public async Task Load_OnlyFindsEntriesOfThatRequest()
    {
        var entry = await AddAsync("users/get-user.yaml");

        Assert.Null((await _store.LoadAsync(_root, P("users/other.yaml"), entry.Entry.Id)).Value);
        Assert.Null((await _store.LoadAsync(_root, P("users/get-user.yaml"), "20200101T000000000-ff")).Value);
    }

    [Fact]
    public async Task Add_KeepsTheNewestEntriesOfEachRequest()
    {
        var limits = new HistoryLimits(2, long.MaxValue);
        var first = await AddAsync("a.yaml", limits);
        var second = await AddAsync("a.yaml", limits);
        var third = await AddAsync("a.yaml", limits);
        var other = await AddAsync("b.yaml", limits);

        Assert.Equal([third.Entry.Id, second.Entry.Id], await IdsAsync("a.yaml"));
        Assert.Equal([other.Entry.Id], await IdsAsync("b.yaml"));
        Assert.False(File.Exists(Path.Combine(HistoryFolder, first.Entry.Id + ".json")));
    }

    [Fact]
    public async Task Add_DropsTheOldestEntriesOfTheWorkspaceBeyondTheTotalSize()
    {
        var first = await AddAsync("a.yaml");
        var entrySize = new FileInfo(Path.Combine(HistoryFolder, first.Entry.Id + ".json")).Length;
        var limits = new HistoryLimits(100, entrySize * 2 + entrySize / 2);
        var second = await AddAsync("b.yaml", limits);
        var third = await AddAsync("a.yaml", limits);

        Assert.Equal([third.Entry.Id], await IdsAsync("a.yaml"));
        Assert.Equal([second.Entry.Id], await IdsAsync("b.yaml"));

        // The entry just added is kept even when it alone is over the limit.
        var huge = await AddAsync("c.yaml", new HistoryLimits(100, 1), body: new string('x', 1000));
        Assert.Equal([huge.Entry.Id], await IdsAsync("c.yaml"));
        Assert.Empty(await IdsAsync("a.yaml"));
    }

    [Fact]
    public async Task Move_FollowsRequestsAndFolders()
    {
        var request = await AddAsync("users/get-user.yaml");
        var nested = await AddAsync("users/admin/list.yaml");
        var sibling = await AddAsync("users-old/keep.yaml");

        Assert.True(_store.Move(_root, P("users/get-user.yaml"), P("people/get-person.yaml")).IsSuccess);
        Assert.True(_store.Move(_root, P("users"), P("accounts")).IsSuccess);

        Assert.Equal([request.Entry.Id], await IdsAsync("people/get-person.yaml"));
        Assert.Equal([nested.Entry.Id], await IdsAsync("accounts/admin/list.yaml"));
        Assert.Equal([sibling.Entry.Id], await IdsAsync("users-old/keep.yaml"));
        Assert.Empty(await IdsAsync("users/admin/list.yaml"));
        Assert.Equal("{}", (await _store.LoadAsync(_root, P("accounts/admin/list.yaml"), nested.Entry.Id)).Value!.Response.Body);
    }

    [Fact]
    public async Task Remove_DeletesTheHistoryOfARequestOrAFolder()
    {
        var request = await AddAsync("users/get-user.yaml");
        await AddAsync("orders/a.yaml");
        await AddAsync("orders/sub/b.yaml");
        var kept = await AddAsync("orders-archive/c.yaml");

        _store.Remove(_root, P("users/get-user.yaml"));
        _store.Remove(_root, P("orders"));

        Assert.Empty(await IdsAsync("users/get-user.yaml"));
        Assert.Empty(await IdsAsync("orders/a.yaml"));
        Assert.Empty(await IdsAsync("orders/sub/b.yaml"));
        Assert.Equal([kept.Entry.Id], await IdsAsync("orders-archive/c.yaml"));
        Assert.False(File.Exists(Path.Combine(HistoryFolder, request.Entry.Id + ".json")));
    }

    [Fact]
    public async Task Delete_RemovesOneEntryOrAll()
    {
        var first = await AddAsync("a.yaml");
        var second = await AddAsync("a.yaml");

        await _store.DeleteAsync(_root, P("a.yaml"), first.Entry.Id);
        Assert.Equal([second.Entry.Id], await IdsAsync("a.yaml"));

        await _store.DeleteAsync(_root, P("a.yaml"), null);
        Assert.Empty(await IdsAsync("a.yaml"));
        Assert.False(File.Exists(Path.Combine(HistoryFolder, JsonHistoryStore.IndexFileName)));
        Assert.Empty(Directory.GetFiles(HistoryFolder));
    }

    [Fact]
    public async Task UnreadableIndex_ReadsAsEmpty()
    {
        Directory.CreateDirectory(HistoryFolder);
        File.WriteAllText(Path.Combine(HistoryFolder, JsonHistoryStore.IndexFileName), "not json");

        Assert.Empty(await IdsAsync("a.yaml"));
        var entry = await AddAsync("a.yaml");
        Assert.Equal([entry.Entry.Id], await IdsAsync("a.yaml"));
    }
}
