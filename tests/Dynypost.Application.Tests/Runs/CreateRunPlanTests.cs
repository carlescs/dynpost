using Dynypost.Application.Common;
using Dynypost.Application.Runs;
using Dynypost.Application.Workspaces;
using Dynypost.Domain.Runs;
using Dynypost.Domain.Workspaces;
using NSubstitute;

namespace Dynypost.Application.Tests.Runs;

public class CreateRunPlanTests
{
    private const string Root = "/work/api";

    private readonly IRunPlanStore _store = Substitute.For<IRunPlanStore>();
    private readonly IRunPlanFormat _format = Substitute.For<IRunPlanFormat>();
    private readonly CurrentWorkspace _current = new();

    public CreateRunPlanTests()
    {
        _current.Open(Root);
        // The first line of the text is the plan's name.
        _format.Parse(Arg.Any<string>()).Returns(call => Plan(((string)call[0]).Split('\n')[0]));
        _store.WriteAsync(Root, Arg.Any<WorkspacePath>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
    }

    private static RunPlan Plan(string name) => new(
        name,
        [WorkspacePath.Parse("requests/get.yaml")],
        new RunLoad(1, 1, null, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, null),
        null,
        new Dictionary<string, RunAssertion>(),
        RunThresholds.None);

    private CreateRunPlan Create() => new(_store, _format, _current);

    [Fact]
    public async Task SavesThePlanInAFileNamedAfterIt()
    {
        var created = await Create().ExecuteAsync("plans/perf", "Checkout smoke\r\nload: 1");

        Assert.Equal("plans/perf/checkout-smoke.run.yaml", created.Value.Path);
        Assert.Equal("Checkout smoke\nload: 1\n", created.Value.Yaml);
        Assert.Null(created.Value.Error);
        await _store.Received(1).WriteAsync(Root, WorkspacePath.Parse("plans/perf/checkout-smoke.run.yaml"), "Checkout smoke\nload: 1\n", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NeverReplacesAnotherPlan()
    {
        _store.Exists(Root, WorkspacePath.Parse("plans/checkout-smoke.run.yaml")).Returns(true);
        _store.Exists(Root, WorkspacePath.Parse("plans/checkout-smoke-2.run.yaml")).Returns(true);

        var created = await Create().ExecuteAsync("plans", "Checkout smoke\n");

        Assert.Equal("plans/checkout-smoke-3.run.yaml", created.Value.Path);
        await _store.DidNotReceive().WriteAsync(Root, WorkspacePath.Parse("plans/checkout-smoke.run.yaml"), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("requests")]
    [InlineData("plans/../requests")]
    [InlineData("elsewhere")]
    public async Task OnlyCreatesPlansInPlans(string folder)
    {
        var created = await Create().ExecuteAsync(folder, "Smoke\n");

        Assert.Equal(ErrorCodes.InvalidPath, created.Error!.Code);
        await _store.DidNotReceiveWithAnyArgs().WriteAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task UnreadableYaml_IsRejected()
    {
        _format.Parse("not: [yaml\n").Returns(new Error(ErrorCodes.InvalidFile, "Bad YAML."));

        var created = await Create().ExecuteAsync("plans", "not: [yaml");

        Assert.Equal(ErrorCodes.InvalidFile, created.Error!.Code);
        await _store.DidNotReceiveWithAnyArgs().WriteAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task WithoutAWorkspace_NothingIsCreated()
    {
        var created = await new CreateRunPlan(_store, _format, new CurrentWorkspace()).ExecuteAsync("plans", "Smoke\n");

        Assert.Equal(ErrorCodes.NoWorkspace, created.Error!.Code);
    }
}
