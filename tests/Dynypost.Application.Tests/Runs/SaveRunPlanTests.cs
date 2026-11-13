using Dynypost.Application.Common;
using Dynypost.Application.Runs;
using Dynypost.Application.Sharing;
using Dynypost.Application.Workspaces;
using Dynypost.Domain.Runs;
using Dynypost.Domain.Workspaces;
using NSubstitute;

namespace Dynypost.Application.Tests.Runs;

public class SaveRunPlanTests
{
    private const string Root = "/work/api";

    private readonly IRunPlanStore _store = Substitute.For<IRunPlanStore>();
    private readonly IRunPlanFormat _format = Substitute.For<IRunPlanFormat>();
    private readonly IRunStore _runs = Substitute.For<IRunStore>();
    private readonly RunRegistry _registry = new();
    private readonly ILocalOnlyFiles _localFiles = Substitute.For<ILocalOnlyFiles>();
    private readonly CurrentWorkspace _current = new();

    public SaveRunPlanTests()
    {
        _current.Open(Root);
        _format.Parse(Arg.Any<string>()).Returns(call => Plan(((string)call[0]).Split('\n')[0]));
        _store.WriteAsync(Root, Arg.Any<WorkspacePath>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        _store.Move(Root, Arg.Any<WorkspacePath>(), Arg.Any<WorkspacePath>()).Returns(Unit.Value);
        _runs.MovePlanAsync(Root, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Unit.Value);
    }

    private static RunPlan Plan(string name) => new(
        name,
        [WorkspacePath.Parse("get.yaml")],
        new RunLoad(1, 1, null, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, null),
        null,
        new Dictionary<string, RunAssertion>(),
        RunThresholds.None);

    private SaveRunPlan Create() => new(_store, _format, _runs, _registry, _localFiles, _current);

    private void OnDisk(string path, string text)
    {
        var parsed = WorkspacePath.Parse(path);
        _store.Exists(Root, parsed).Returns(true);
        _store.ReadAsync(Root, parsed, Arg.Any<CancellationToken>()).Returns(text);
    }

    [Fact]
    public async Task RenamesTheFileWhenItIsTheSlugOfTheOldName()
    {
        OnDisk("plans/perf/plan-1.run.yaml", "Plan 1\n");

        var saved = await Create().ExecuteAsync("plans/perf/plan-1.run.yaml", "Checkout smoke\n");

        Assert.Equal("plans/perf/checkout-smoke.run.yaml", saved.Value.Path);
        _store.Received().Move(Root, WorkspacePath.Parse("plans/perf/plan-1.run.yaml"), WorkspacePath.Parse("plans/perf/checkout-smoke.run.yaml"));
        await _runs.Received().MovePlanAsync(Root, "plans/perf/plan-1.run.yaml", "plans/perf/checkout-smoke.run.yaml", Arg.Any<CancellationToken>());
        _localFiles.Received(1).Move(Root, WorkspacePath.Parse("plans/perf/plan-1.run.yaml"), WorkspacePath.Parse("plans/perf/checkout-smoke.run.yaml"));
    }

    [Fact]
    public async Task PicksAFreeNameAndKeepsTheExtension()
    {
        OnDisk("plans/smoke.run.yml", "Smoke\n");
        _store.Exists(Root, WorkspacePath.Parse("plans/load.run.yml")).Returns(true);

        var saved = await Create().ExecuteAsync("plans/smoke.run.yml", "Load\n");

        Assert.Equal("plans/load-2.run.yml", saved.Value.Path);
    }

    [Fact]
    public async Task KeepsHandPickedFileNames()
    {
        OnDisk("plans/nightly.run.yaml", "Smoke\n");

        var saved = await Create().ExecuteAsync("plans/nightly.run.yaml", "Load\n");

        Assert.Equal("plans/nightly.run.yaml", saved.Value.Path);
        _store.DidNotReceive().Move(Arg.Any<string>(), Arg.Any<WorkspacePath>(), Arg.Any<WorkspacePath>());
    }

    [Fact]
    public async Task KeepsTheFileWhenTheNameIsUnchangedOrNew()
    {
        OnDisk("plans/smoke.run.yaml", "Smoke\n");

        Assert.Equal("plans/smoke.run.yaml", (await Create().ExecuteAsync("plans/smoke.run.yaml", "Smoke\nmore")).Value.Path);
        Assert.Equal("plans/new.run.yaml", (await Create().ExecuteAsync("plans/new.run.yaml", "Other\n")).Value.Path);
        _store.DidNotReceive().Move(Arg.Any<string>(), Arg.Any<WorkspacePath>(), Arg.Any<WorkspacePath>());
    }

    [Fact]
    public async Task KeepsTheFileWhenTheNewTextDoesNotParse()
    {
        OnDisk("plans/smoke.run.yaml", "Smoke\n");
        _format.Parse("broken\n").Returns(new Error(ErrorCodes.InvalidFile, "bad"));

        var saved = await Create().ExecuteAsync("plans/smoke.run.yaml", "broken");

        Assert.Equal("plans/smoke.run.yaml", saved.Value.Path);
        Assert.Equal("bad", saved.Value.Error);
    }

    [Fact]
    public async Task KeepsTheFileWhileThePlanIsBeingRun()
    {
        OnDisk("plans/smoke.run.yaml", "Smoke\n");
        using var reservation = _registry.TryReserve(Root, "plans/smoke.run.yaml");

        var saved = await Create().ExecuteAsync("plans/smoke.run.yaml", "Load\n");

        Assert.Equal("plans/smoke.run.yaml", saved.Value.Path);
    }
}
