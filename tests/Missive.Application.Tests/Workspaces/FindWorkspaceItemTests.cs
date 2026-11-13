using Missive.Application.Common;
using Missive.Application.Environments;
using Missive.Application.Runs;
using Missive.Application.Workspaces;
using Missive.Domain.Workspaces;
using NSubstitute;

namespace Missive.Application.Tests.Workspaces;

public class FindWorkspaceItemTests
{
    private const string Root = "/work/api";

    private readonly IWorkspaceRepository _repository = Substitute.For<IWorkspaceRepository>();
    private readonly IRunPlanStore _plans = Substitute.For<IRunPlanStore>();
    private readonly IEnvironmentStore _environments = Substitute.For<IEnvironmentStore>();
    private readonly CurrentWorkspace _current = new();

    public FindWorkspaceItemTests() => _current.Open(Root);

    private FindWorkspaceItem Create() => new(_repository, _plans, _environments, _current);

    [Fact]
    public void ARequest_IsFound()
    {
        _repository.FileExists(Root, WorkspacePath.Parse("requests/users/get.yaml")).Returns(true);

        var item = Create().Execute("requests/users/get.yaml");

        Assert.Equal(new WorkspaceItemDto("requests/users/get.yaml", FindWorkspaceItem.Request, "requests/users/get.yaml"), item.Value);
    }

    [Fact]
    public void ARunPlan_IsFound()
    {
        _plans.Exists(Root, WorkspacePath.Parse("plans/perf/smoke.run.yaml")).Returns(true);

        var item = Create().Execute("plans/perf/smoke.run.yaml");

        Assert.Equal(FindWorkspaceItem.RunPlan, item.Value.Kind);
        Assert.Equal("plans/perf/smoke.run.yaml", item.Value.Id);
    }

    [Fact]
    public void AnEnvironment_IsFoundByItsFileName()
    {
        _environments.Exists(Root, "dev.yaml").Returns(true);

        var item = Create().Execute("environments/dev.yaml");

        Assert.Equal(new WorkspaceItemDto("environments/dev.yaml", FindWorkspaceItem.Environment, "dev.yaml"), item.Value);
    }

    [Fact]
    public void AMissingFile_IsNotFound()
    {
        var item = Create().Execute("requests/missing.yaml");

        Assert.Equal(ErrorCodes.NotFound, item.Error!.Code);
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("requests")]
    [InlineData("environments/nested/dev.yaml")]
    [InlineData("../outside.yaml")]
    [InlineData("")]
    public void OtherPaths_AreRejected(string path)
    {
        var item = Create().Execute(path);

        Assert.Equal(ErrorCodes.InvalidPath, item.Error!.Code);
    }

    [Fact]
    public void WithoutAWorkspace_NothingIsFound()
    {
        var item = new FindWorkspaceItem(_repository, _plans, _environments, new CurrentWorkspace()).Execute("requests/a.yaml");

        Assert.Equal(ErrorCodes.NoWorkspace, item.Error!.Code);
    }
}
