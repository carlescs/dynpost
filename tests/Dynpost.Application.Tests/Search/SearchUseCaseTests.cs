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
    private readonly CurrentWorkspace _current = new();

    public SearchUseCaseTests()
    {
        _current.Open(Root);
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

    private static RequestFileEntry File(string path, HttpRequestDefinition request) =>
        new(WorkspacePath.Parse(path), request);

    private void Text(string path, string text) =>
        _repository.ReadTextAsync(Root, WorkspacePath.Parse(path), Arg.Any<CancellationToken>()).Returns(Result<string>.Success(text));

    [Fact]
    public async Task QuickOpen_RanksTheBestNameMatchFirst()
    {
        var items = await new QuickOpen(_repository, _current).ExecuteAsync("getu");

        var first = items.Value[0];
        Assert.Equal("requests/users/get-user.yaml", first.Path);
        Assert.Equal("GET", first.Method);
        Assert.Equal("{{base}}/users/1", first.Url);
        Assert.Equal([0, 1, 2, 4], first.NameMatches);
    }

    [Fact]
    public async Task QuickOpen_MatchesEachWordAgainstAnyField()
    {
        var items = await new QuickOpen(_repository, _current).ExecuteAsync("post users");

        var item = Assert.Single(items.Value);
        Assert.Equal("Create user", item.Name);
        Assert.NotEmpty(item.UrlMatches);
    }

    [Fact]
    public async Task QuickOpen_FindsRequestsByTheirFolder()
    {
        var items = await new QuickOpen(_repository, _current).ExecuteAsync("users/get");

        Assert.Equal("requests/users/get-user.yaml", items.Value[0].Path);
        Assert.NotEmpty(items.Value[0].PathMatches);
    }

    [Fact]
    public async Task QuickOpen_WithoutAQuery_ListsEveryRequestInTreeOrder_UnreadableOnesByFileName()
    {
        var items = await new QuickOpen(_repository, _current).ExecuteAsync("");

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
        var items = await new QuickOpen(_repository, new CurrentWorkspace()).ExecuteAsync("x");

        Assert.Equal(ErrorCodes.NoWorkspace, items.Error!.Code);
    }

    [Fact]
    public async Task Search_GroupsMatchingLinesByFile()
    {
        Text("requests/users/create-user.yaml", "name: Create user\nmethod: POST\nbody:\n  json: |\n    { \"role\": \"admin\" }\n");
        Text("requests/users/get-user.yaml", "name: Get user\nmethod: GET\n");
        Text("requests/broken.yaml", "name: [admin\n");
        Text("requests/health.yaml", "name: Health\n");

        var result = await new SearchWorkspace(_repository, _current).ExecuteAsync("ADMIN");

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

        var result = await new SearchWorkspace(_repository, _current).ExecuteAsync("POST", matchCase: true);

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

        var result = await new SearchWorkspace(_repository, _current).ExecuteAsync("x");

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

        var result = await new SearchWorkspace(_repository, _current).ExecuteAsync("x");

        Assert.Equal("requests/users/create-user.yaml", Assert.Single(result.Value.Files).Path);
    }

    [Fact]
    public async Task Search_WithAnEmptyQuery_FindsNothing()
    {
        var result = await new SearchWorkspace(_repository, _current).ExecuteAsync("");

        Assert.Empty(result.Value.Files);
        await _repository.DidNotReceiveWithAnyArgs().ReadTextAsync(default!, default!, default);
    }
}
