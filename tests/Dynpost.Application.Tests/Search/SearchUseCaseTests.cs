using Dynpost.Application.Common;
using Dynpost.Application.Search;
using Dynpost.Application.Workspaces;
using Dynpost.Domain.Requests;
using Dynpost.Domain.Workspaces;
using NSubstitute;

namespace Dynpost.Application.Tests.Search;

public class SearchUseCaseTests
{
    private const string Root = "/work/api";

    private readonly IWorkspaceRepository _repository = Substitute.For<IWorkspaceRepository>();
    private readonly IFuzzyMatcher _matcher = Substitute.For<IFuzzyMatcher>();
    private readonly CurrentWorkspace _current = new();

    public SearchUseCaseTests()
    {
        _current.Open(Root);
        // Exact occurrences unless a test says otherwise; the real typo tolerance is the adapter's.
        _matcher.Find(default!, default!, default).ReturnsForAnyArgs(call => Occurrences(call.ArgAt<string>(0), call.ArgAt<string>(1)));
        IReadOnlyList<WorkspaceEntry> entries =
        [
            new FolderEntry(WorkspacePath.Parse("requests/users"),
            [
                File("requests/users/create-user.yaml", new HttpRequestDefinition("Create user", RequestMethod.Post, "{{base}}/users")),
                File("requests/users/get-user.yaml", new HttpRequestDefinition("Get user", RequestMethod.Get, "{{base}}/users/1")),
            ]),
            new RequestFileEntry(WorkspacePath.Parse("requests/broken.yaml"), new Error(ErrorCodes.InvalidFile, "Bad YAML")),
            File("requests/health.yaml", new HttpRequestDefinition("Health", RequestMethod.Get, "{{base}}/health")),
        ];
        _repository.ListAsync(Root, Arg.Any<CancellationToken>()).Returns(Result<IReadOnlyList<WorkspaceEntry>>.Success(entries));
    }

    private static IReadOnlyList<FuzzyMatch> Occurrences(string? pattern, string? text)
    {
        var found = new List<FuzzyMatch>();
        if (string.IsNullOrEmpty(pattern) || text is null)
        {
            return found;
        }

        for (var at = text.IndexOf(pattern, StringComparison.Ordinal); at >= 0; at = text.IndexOf(pattern, at + pattern.Length, StringComparison.Ordinal))
        {
            found.Add(new FuzzyMatch(at, pattern.Length, 0));
        }

        return found;
    }

    private QuickOpen CreateQuickOpen() => new(_repository, _matcher, _current);

    private SearchWorkspace CreateSearch() => new(_repository, _matcher, _current);

    private static RequestFileEntry File(string path, HttpRequestDefinition request) =>
        new(WorkspacePath.Parse(path), request);

    private void Text(string path, string text) =>
        _repository.ReadTextAsync(Root, WorkspacePath.Parse(path), Arg.Any<CancellationToken>()).Returns(Result<string>.Success(text));

    [Fact]
    public async Task QuickOpen_RanksTheBestNameMatchFirst()
    {
        var items = await CreateQuickOpen().ExecuteAsync("get");

        var first = items.Value[0];
        Assert.Equal("requests/users/get-user.yaml", first.Path);
        Assert.Equal("GET", first.Method);
        Assert.Equal("{{base}}/users/1", first.Url);
        Assert.Equal([0, 1, 2], first.NameMatches);
    }

    [Fact]
    public async Task QuickOpen_MatchesEachWordAgainstAnyField()
    {
        var items = await CreateQuickOpen().ExecuteAsync("post /users");

        var item = Assert.Single(items.Value);
        Assert.Equal("Create user", item.Name);
        Assert.NotEmpty(item.UrlMatches);
    }

    [Fact]
    public async Task QuickOpen_FindsRequestsByTheirFolder()
    {
        var items = await CreateQuickOpen().ExecuteAsync("users/get");

        Assert.Equal("requests/users/get-user.yaml", items.Value[0].Path);
        Assert.NotEmpty(items.Value[0].PathMatches);
    }

    [Fact]
    public async Task QuickOpen_WithoutAQuery_ListsEveryRequestInTreeOrder_UnreadableOnesByFileName()
    {
        var items = await CreateQuickOpen().ExecuteAsync("");

        Assert.Equal(
            ["requests/users/create-user.yaml", "requests/users/get-user.yaml", "requests/broken.yaml", "requests/health.yaml"],
            items.Value.Select(i => i.Path));
        var broken = items.Value.Single(i => i.Path == "requests/broken.yaml");
        Assert.Equal("broken", broken.Name);
        Assert.Null(broken.Method);
        Assert.Equal("", broken.Url);
    }

    [Fact]
    public async Task QuickOpen_WithoutAWorkspace_Fails()
    {
        var items = await new QuickOpen(_repository, _matcher, new CurrentWorkspace()).ExecuteAsync("x");

        Assert.Equal(ErrorCodes.NoWorkspace, items.Error!.Code);
    }

    [Fact]
    public async Task Search_GroupsMatchingLinesByFile()
    {
        Text("requests/users/create-user.yaml", "name: Create user\nmethod: POST\nbody:\n  json: |\n    { \"role\": \"admin\" }\n");
        Text("requests/users/get-user.yaml", "name: Get user\nmethod: GET\n");
        Text("requests/broken.yaml", "name: [admin\n");
        Text("requests/health.yaml", "name: Health\n");

        var result = await CreateSearch().ExecuteAsync("ADMIN");

        Assert.Equal(["requests/users/create-user.yaml", "requests/broken.yaml"], result.Value.Files.Select(f => f.Path));
        var line = Assert.Single(result.Value.Files[0].Lines);
        Assert.Equal(5, line.Line);
        Assert.Equal("{ \"role\": \"admin\" }", line.Text);
        Assert.Equal([new SearchRangeDto(11, 5)], line.Ranges);
        Assert.Equal("broken", result.Value.Files[1].Name);
        Assert.Equal(2, result.Value.MatchCount);
        Assert.False(result.Value.Truncated);
    }

    [Fact]
    public async Task Search_MatchCase_IsHonoured()
    {
        Text("requests/users/create-user.yaml", "method: POST\n");
        Text("requests/users/get-user.yaml", "name: post office\n");
        Text("requests/broken.yaml", "");
        Text("requests/health.yaml", "");

        var result = await CreateSearch().ExecuteAsync("POST", matchCase: true);

        Assert.Equal("requests/users/create-user.yaml", Assert.Single(result.Value.Files).Path);
    }

    [Fact]
    public async Task Search_StopsAfterTheMostLines()
    {
        var many = string.Concat(Enumerable.Repeat("x\n", SearchWorkspace.MaxLines - 1));
        Text("requests/users/create-user.yaml", many);
        Text("requests/users/get-user.yaml", "x\nx\n");
        Text("requests/broken.yaml", "x\n");
        Text("requests/health.yaml", "");

        var result = await CreateSearch().ExecuteAsync("x");

        Assert.True(result.Value.Truncated);
        Assert.Equal(SearchWorkspace.MaxLines, result.Value.Files.Sum(f => f.Lines.Count));
        Assert.Equal(2, result.Value.Files.Count);
    }

    [Fact]
    public async Task Search_SkipsFilesThatCannotBeRead()
    {
        Text("requests/users/create-user.yaml", "x\n");
        _repository.ReadTextAsync(Root, WorkspacePath.Parse("requests/users/get-user.yaml"), Arg.Any<CancellationToken>())
            .Returns(Result<string>.Failure(ErrorCodes.NotFound, "Gone"));
        Text("requests/broken.yaml", "");
        Text("requests/health.yaml", "");

        var result = await CreateSearch().ExecuteAsync("x");

        Assert.Equal("requests/users/create-user.yaml", Assert.Single(result.Value.Files).Path);
    }

    [Fact]
    public async Task Search_WithAnEmptyQuery_FindsNothing()
    {
        var result = await CreateSearch().ExecuteAsync("");

        Assert.Empty(result.Value.Files);
        await _repository.DidNotReceiveWithAnyArgs().ReadTextAsync(default!, default!, default);
    }

    [Fact]
    public async Task QuickOpen_PrefersExactMatchesToTypos()
    {
        _matcher.Find("user", "health", 1).Returns([new FuzzyMatch(0, 4, 1)]);

        var items = await CreateQuickOpen().ExecuteAsync("User");

        Assert.Equal(["Get user", "Create user", "Health"], items.Value.Select(i => i.Name));
        Assert.Equal([0, 1, 2, 3], items.Value[2].NameMatches);
    }

    [Fact]
    public async Task FuzzySearch_PutsTheFilesWithTheFewestTyposFirst_IgnoringCase()
    {
        Text("requests/users/create-user.yaml", "headers:\n  Authorisation: x\n");
        Text("requests/users/get-user.yaml", "body: plain\n");
        Text("requests/broken.yaml", "");
        Text("requests/health.yaml", "headers:\n  Authorization: Bearer x\n");
        _matcher.Find("authorization", "  authorisation: x", 2).Returns([new FuzzyMatch(2, 13, 1)]);

        var result = await CreateSearch().ExecuteAsync("Authorization", fuzzy: true);

        Assert.Equal(["requests/health.yaml", "requests/users/create-user.yaml"], result.Value.Files.Select(f => f.Path));
        var line = Assert.Single(result.Value.Files[1].Lines);
        Assert.Equal(2, line.Line);
        Assert.Equal("Authorisation: x", line.Text);
        Assert.Equal([new SearchRangeDto(0, 13)], line.Ranges);
        Assert.Equal(2, result.Value.MatchCount);
    }

    [Fact]
    public async Task FuzzySearch_MatchCase_SearchesTheTextAsWritten()
    {
        Text("requests/users/create-user.yaml", "method: POST\n");
        Text("requests/users/get-user.yaml", "name: post office\n");
        Text("requests/broken.yaml", "");
        Text("requests/health.yaml", "");

        var result = await CreateSearch().ExecuteAsync("POST", matchCase: true, fuzzy: true);

        Assert.Equal("requests/users/create-user.yaml", Assert.Single(result.Value.Files).Path);
        _matcher.Received().Find("POST", "method: POST", 1);
    }

    [Fact]
    public void Tolerance_GrowsWithTheLengthOfTheQuery()
    {
        Assert.Equal(0, FuzzyTolerance.MaxDistance(3));
        Assert.Equal(1, FuzzyTolerance.MaxDistance(4));
        Assert.Equal(1, FuzzyTolerance.MaxDistance(6));
        Assert.Equal(2, FuzzyTolerance.MaxDistance(7));
    }

    [Fact]
    public async Task PlainSearch_FindsOnlyTheExactText()
    {
        Text("requests/users/create-user.yaml", "");
        Text("requests/users/get-user.yaml", "");
        Text("requests/broken.yaml", "");
        Text("requests/health.yaml", "Content-Type: text/plain\n");

        _matcher.Find(default!, default!, default).ReturnsForAnyArgs([new FuzzyMatch(0, 5, 1)]);

        var result = await CreateSearch().ExecuteAsync("ctype");

        Assert.Empty(result.Value.Files);
    }
}
