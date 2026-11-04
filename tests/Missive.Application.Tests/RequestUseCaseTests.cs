using Missive.Application.Common;
using Missive.Application.Requests;
using Missive.Application.Workspaces;
using Missive.Domain.Requests;
using Missive.Domain.Workspaces;
using NSubstitute;

namespace Missive.Application.Tests;

public class RequestUseCaseTests
{
    private const string Root = "/work/my-api";

    private readonly IWorkspaceRepository _repository = Substitute.For<IWorkspaceRepository>();
    private readonly CurrentWorkspace _current = new();

    public RequestUseCaseTests()
    {
        _current.Open(Root);
        _repository.WriteRequestAsync(Root, Arg.Any<WorkspacePath>(), Arg.Any<HttpRequestDefinition>(), Arg.Any<CancellationToken>())
            .Returns(Result<bool>.Success(true));
        _repository.MoveFile(Root, Arg.Any<WorkspacePath>(), Arg.Any<WorkspacePath>()).Returns(Unit.Value);
    }

    [Fact]
    public async Task LoadRequest_ReturnsTheRequestAsDto()
    {
        var path = WorkspacePath.Parse("users/get-user.yaml");
        _repository.FileExists(Root, path).Returns(true);
        _repository.ReadRequestAsync(Root, path, Arg.Any<CancellationToken>()).Returns(new HttpRequestDefinition(
            "Get user", RequestMethod.Get, "/users/1",
            headers: [new KeyValueEntry("Accept", "application/json", Enabled: false)],
            auth: new ApiKeyAuth("X-Key", "{{key}}", ApiKeyLocation.Query)));

        var result = await new LoadRequest(_repository, _current).ExecuteAsync("users/get-user.yaml");

        Assert.True(result.IsSuccess);
        Assert.Equal("GET", result.Value.Method);
        Assert.Equal(new KeyValueDto("Accept", "application/json", false), Assert.Single(result.Value.Headers));
        Assert.Equal(new AuthDto("apiKey", Key: "X-Key", Value: "{{key}}", In: "query"), result.Value.Auth);
        Assert.Equal(new BodyDto("none", ""), result.Value.Body);
    }

    [Fact]
    public async Task LoadRequest_RejectsPathsOutsideTheWorkspace()
    {
        var result = await new LoadRequest(_repository, _current).ExecuteAsync("../other/x.yaml");

        Assert.Equal(ErrorCodes.InvalidPath, result.Error?.Code);
        await _repository.DidNotReceiveWithAnyArgs().ReadRequestAsync(default!, default!, default);
    }

    [Fact]
    public async Task CreateRequest_WritesASlugNamedFileInTheFolder()
    {
        var result = await new CreateRequest(_repository, _current).ExecuteAsync("users", RequestDtoFactory.Create());

        Assert.True(result.IsSuccess);
        Assert.Equal("users/create-user.yaml", result.Value.Path);
        await _repository.Received(1).WriteRequestAsync(
            Root,
            WorkspacePath.Parse("users/create-user.yaml"),
            Arg.Is<HttpRequestDefinition>(r => r.Name == "Create user" && r.Method == RequestMethod.Post && r.Body.Type == BodyType.Json),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateRequest_PicksAFreeNameWhenTheSlugIsTaken()
    {
        _repository.FileExists(Root, WorkspacePath.Parse("create-user.yaml")).Returns(true);
        _repository.FileExists(Root, WorkspacePath.Parse("create-user-2.yaml")).Returns(true);

        var result = await new CreateRequest(_repository, _current).ExecuteAsync("", RequestDtoFactory.Create());

        Assert.Equal("create-user-3.yaml", result.Value.Path);
    }

    [Fact]
    public async Task CreateRequest_DropsBlankRowsAndRejectsInvalidRequests()
    {
        var blankRows = RequestDtoFactory.Create() with { Params = [new KeyValueDto("", "", true)] };
        var created = await new CreateRequest(_repository, _current).ExecuteAsync("", blankRows);
        Assert.Empty(created.Value.Request.Params);

        var unnamed = RequestDtoFactory.Create(name: "  ");
        var invalid = await new CreateRequest(_repository, _current).ExecuteAsync("", unnamed);
        Assert.Equal(ErrorCodes.InvalidRequest, invalid.Error?.Code);

        var badMethod = await new CreateRequest(_repository, _current).ExecuteAsync("", RequestDtoFactory.Create(method: "FETCH"));
        Assert.Equal(ErrorCodes.InvalidRequest, badMethod.Error?.Code);
    }

    [Fact]
    public async Task SaveRequest_WritesInPlaceWhenTheNameIsUnchanged()
    {
        var path = WorkspacePath.Parse("users/create-user.yaml");
        GivenExisting(path, "Create user");

        var result = await new SaveRequest(_repository, _current).ExecuteAsync(path.Value, RequestDtoFactory.Create());

        Assert.Equal(path.Value, result.Value.Path);
        await _repository.Received(1).WriteRequestAsync(Root, path, Arg.Any<HttpRequestDefinition>(), Arg.Any<CancellationToken>());
        _repository.DidNotReceiveWithAnyArgs().MoveFile(default!, default!, default!);
    }

    [Fact]
    public async Task SaveRequest_RenamesASlugNamedFileWhenTheRequestIsRenamed()
    {
        var path = WorkspacePath.Parse("users/create-user.yaml");
        GivenExisting(path, "Create user");

        var result = await new SaveRequest(_repository, _current).ExecuteAsync(path.Value, RequestDtoFactory.Create(name: "Add user"));

        Assert.Equal("users/add-user.yaml", result.Value.Path);
        _repository.Received(1).MoveFile(Root, path, WorkspacePath.Parse("users/add-user.yaml"));
    }

    [Fact]
    public async Task SaveRequest_KeepsHandPickedFileNames()
    {
        var path = WorkspacePath.Parse("users/my-special-name.yaml");
        GivenExisting(path, "Create user");

        var result = await new SaveRequest(_repository, _current).ExecuteAsync(path.Value, RequestDtoFactory.Create(name: "Add user"));

        Assert.Equal(path.Value, result.Value.Path);
        _repository.DidNotReceiveWithAnyArgs().MoveFile(default!, default!, default!);
    }

    [Fact]
    public async Task SaveRequest_RejectsNonRequestFiles()
    {
        var result = await new SaveRequest(_repository, _current).ExecuteAsync("missive.yaml", RequestDtoFactory.Create());

        Assert.Equal(ErrorCodes.InvalidPath, result.Error?.Code);
    }

    private void GivenExisting(WorkspacePath path, string name)
    {
        _repository.FileExists(Root, path).Returns(true);
        _repository.ReadRequestAsync(Root, path, Arg.Any<CancellationToken>())
            .Returns(new HttpRequestDefinition(name, RequestMethod.Post, "/users"));
    }
}
