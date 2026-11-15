using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Dynpost.Application.Common;
using Dynpost.Application.Search;
using Dynpost.Application.Workspaces;
using Dynpost.Desktop.Bridge;
using Dynpost.Domain.Requests;
using Dynpost.Domain.Workspaces;
using NSubstitute;

namespace Dynpost.Desktop.Tests.Bridge;

/// <summary>Quick open and workspace search, through the real router and use cases.</summary>
public sealed class SearchBridgeTests : IDisposable
{
    private const string Root = "/work/my-api";

    private readonly IWorkspaceRepository _repository = Substitute.For<IWorkspaceRepository>();
    private readonly ServiceProvider _services;
    private readonly BridgeRouter _router;

    public SearchBridgeTests()
    {
        _services = new ServiceCollection()
            .AddSingleton(_repository)
            .AddSingleton<CurrentWorkspace>()
            .AddTransient<QuickOpen>()
            .AddTransient<SearchWorkspace>()
            .BuildServiceProvider();
        _services.GetRequiredService<CurrentWorkspace>().Open(Root);
        _router = BridgeHandlers.Create(_services, Substitute.For<IFileDialogs>(), Substitute.For<IAppWindow>());

        var path = WorkspacePath.Parse("requests/get-user.yaml");
        IReadOnlyList<WorkspaceEntry> entries = [new RequestFileEntry(path, new HttpRequestDefinition("Get user", RequestMethod.Get, "/users/1"))];
        _repository.ListAsync(Root, Arg.Any<CancellationToken>()).Returns(Result<IReadOnlyList<WorkspaceEntry>>.Success(entries));
        _repository.ReadTextAsync(Root, path, Arg.Any<CancellationToken>())
            .Returns(Result<string>.Success("name: Get user\nmethod: GET\nurl: /users/1\n"));
    }

    public void Dispose() => _services.Dispose();

    [Fact]
    public async Task QuickOpen_SendsEveryFieldTheClientReads()
    {
        var response = await SendAsync("workspace.quickOpen", """{ "query": "user" }""");

        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        var item = Assert.Single(response.GetProperty("payload").EnumerateArray());
        // The names of models/messages.ts QuickOpenItemDto, in the client's camel case.
        string[] fields = ["path", "name", "method", "url", "nameMatches", "pathMatches", "urlMatches"];
        Assert.Equal(fields.Order(), item.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal("requests/get-user.yaml", item.GetProperty("path").GetString());
    }

    [Fact]
    public async Task Search_SendsEveryFieldTheClientReads()
    {
        var response = await SendAsync("workspace.search", """{ "query": "users", "matchCase": true }""");

        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        var payload = response.GetProperty("payload");
        // The names of models/messages.ts WorkspaceSearchDto, SearchFileDto, SearchLineDto and SearchRangeDto.
        Assert.Equal(["files", "matchCount", "truncated"], payload.EnumerateObject().Select(p => p.Name).Order());
        var file = Assert.Single(payload.GetProperty("files").EnumerateArray());
        Assert.Equal(["lines", "method", "name", "path"], file.EnumerateObject().Select(p => p.Name).Order());
        var line = Assert.Single(file.GetProperty("lines").EnumerateArray());
        Assert.Equal(["line", "ranges", "text"], line.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(3, line.GetProperty("line").GetInt32());
        var range = Assert.Single(line.GetProperty("ranges").EnumerateArray());
        Assert.Equal(["length", "start"], range.EnumerateObject().Select(p => p.Name).Order());
    }

    private async Task<JsonElement> SendAsync(string type, string payload)
    {
        var json = await _router.HandleAsync($$"""{ "id": "1", "type": "{{type}}", "payload": {{payload}} }""");
        return JsonDocument.Parse(json!).RootElement;
    }
}
