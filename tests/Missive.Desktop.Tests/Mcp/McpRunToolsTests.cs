using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Missive.Application.Captures;
using Missive.Application.Common;
using Missive.Application.Environments;
using Missive.Application.Http;
using Missive.Application.OAuth2;
using Missive.Application.Runs;
using Missive.Application.Sharing;
using Missive.Application.Workspaces;
using Missive.Desktop.Bridge;
using Missive.Desktop.Mcp;
using Missive.Domain.Http;
using Missive.Domain.Requests;
using Missive.Domain.Runs;
using Missive.Domain.Workspaces;
using NSubstitute;

namespace Missive.Desktop.Tests.Mcp;

/// <summary>The run plan and load run tools, through the real protocol and use cases, with mocked ports.</summary>
public sealed class McpRunToolsTests : IDisposable
{
    private const string Root = "/work/my-api";
    private const string PlanFile = "plans/perf/smoke.run.yaml";

    private readonly IRunPlanStore _plans = Substitute.For<IRunPlanStore>();
    private readonly IRunPlanFormat _format = Substitute.For<IRunPlanFormat>();
    private readonly IWorkspaceRepository _repository = Substitute.For<IWorkspaceRepository>();
    private readonly IHttpSender _sender = Substitute.For<IHttpSender>();
    private readonly IRunStore _runs = Substitute.For<IRunStore>();
    private readonly List<JsonNode> _events = [];
    private readonly ServiceProvider _services;
    private readonly McpProtocol _protocol;

    public McpRunToolsTests()
    {
        var state = Substitute.For<IWorkspaceStateStore>();
        state.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(WorkspaceState.Default);
        _plans.ReadAsync(Root, WorkspacePath.Parse(PlanFile), Arg.Any<CancellationToken>()).Returns("plan");
        _plans.Exists(Root, WorkspacePath.Parse(PlanFile)).Returns(true);
        _plans.Delete(Root, WorkspacePath.Parse(PlanFile)).Returns(Unit.Value);
        _repository.FileExists(Root, Arg.Any<WorkspacePath>()).Returns(true);
        _repository.ReadRequestAsync(Root, Arg.Any<WorkspacePath>(), Arg.Any<CancellationToken>())
            .Returns(new HttpRequestDefinition("Get", RequestMethod.Get, "https://api.test/users"));
        _sender.SendAsync(Arg.Any<OutgoingRequest>(), Arg.Any<CancellationToken>())
            .Returns(new HttpResponseData(200, "OK", [], Encoding.UTF8.GetBytes("ok"), false, TimeSpan.FromMilliseconds(5)));
        _runs.SaveAsync(Root, Arg.Any<RunResultDto>(), Arg.Any<CancellationToken>()).Returns(Unit.Value);
        _runs.SaveSamplesAsync(Root, Arg.Any<string>(), Arg.Any<IReadOnlyList<SampleDto>>(), Arg.Any<CancellationToken>()).Returns(Unit.Value);
        _runs.DeletePlanAsync(Root, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Unit.Value);

        _services = new ServiceCollection()
            .AddEmptyFolderDefaults()
            .AddSingleton(_plans)
            .AddSingleton(_format)
            .AddSingleton(Substitute.For<IRunDataReader>())
            .AddSingleton(Substitute.For<IRunBodyReader>())
            .AddSingleton(_repository)
            .AddSingleton(_sender)
            .AddSingleton(_runs)
            .AddSingleton(state)
            .AddSingleton(Substitute.For<IEnvironmentStore>())
            .AddSingleton(Substitute.For<ISecretStore>())
            .AddSingleton(Substitute.For<ICapturedValueStore>())
            .AddSingleton(Substitute.For<IOAuth2TokenStore>())
            .AddSingleton(Substitute.For<IAuthorizationBrowser>())
            .AddSingleton(Substitute.For<ILocalOnlyFiles>())
            .AddSingleton(TimeProvider.System)
            .AddSingleton<RunRegistry>()
            .AddSingleton<CurrentWorkspace>()
            .AddSingleton<OAuth2Tokens>()
            .AddTransient<ActiveEnvironment>()
            .AddTransient<StartRun>()
            .AddTransient<GetRunProgress>()
            .AddTransient<WaitForRun>()
            .AddTransient<CancelRun>()
            .AddTransient<DeleteRunPlan>()
            .AddTransient<CreateRunPlan>()
            .BuildServiceProvider();
        _services.GetRequiredService<CurrentWorkspace>().Open(Root);

        var events = new HostEvents();
        events.Connect(message =>
        {
            lock (_events)
            {
                _events.Add(JsonNode.Parse(message)!);
            }

            return Task.CompletedTask;
        });
        _protocol = new McpProtocol(McpTools.Create(_services, events), "1.0.0");
    }

    public void Dispose() => _services.Dispose();

    private void UsePlan(int threads, int iterations) =>
        _format.Parse("plan").Returns(new RunPlan(
            "Smoke",
            [WorkspacePath.Parse("requests/users.yaml")],
            new RunLoad(threads, iterations, null, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, null),
            null,
            new Dictionary<string, RunAssertion>(),
            RunThresholds.None));

    [Fact]
    public async Task StartRun_TellsTheUiAndTheRunCanBeAwaited()
    {
        UsePlan(threads: 2, iterations: 3);

        var started = await CallToolAsync("start_run", $$"""{ "path": "{{PlanFile}}" }""");
        Assert.False(started["isError"]!.GetValue<bool>(), Text(started));
        var runId = JsonNode.Parse(Text(started))!["runId"]!.GetValue<string>();

        var message = Assert.Single(_events);
        Assert.Equal("run.started", message["event"]!.GetValue<string>());
        Assert.Equal(PlanFile, message["payload"]!["path"]!.GetValue<string>());
        Assert.Equal(runId, message["payload"]!["runId"]!.GetValue<string>());

        var result = await CallToolAsync("get_run", $$"""{ "runId": "{{runId}}", "waitSeconds": 10 }""");
        var run = JsonNode.Parse(Text(result))!;
        Assert.Equal("completed", run["state"]!.GetValue<string>());
        Assert.Equal(6, run["total"]!["samples"]!.GetValue<long>());
    }

    [Fact]
    public async Task ALargeRun_NeedsConfirmationAndStartsNothing()
    {
        UsePlan(threads: RunPlan.WarnThreads + 1, iterations: 1);

        var started = await CallToolAsync("start_run", $$"""{ "path": "{{PlanFile}}" }""");

        Assert.True(started["isError"]!.GetValue<bool>());
        Assert.StartsWith(RunErrorCodes.ConfirmationRequired + ":", Text(started));
        Assert.Empty(_events);
        await _sender.DidNotReceiveWithAnyArgs().SendAsync(default!, default);
    }

    [Fact]
    public async Task DeleteRunPlan_TellsTheUiWhatChanged()
    {
        var result = await CallToolAsync("delete_run_plan", $$"""{ "path": "{{PlanFile}}" }""");

        Assert.False(result["isError"]!.GetValue<bool>(), Text(result));
        var message = Assert.Single(_events);
        Assert.Equal("workspace.changed", message["event"]!.GetValue<string>());
        Assert.Equal(PlanFile, Assert.Single(message["payload"]!["paths"]!.AsArray())!.GetValue<string>());
    }

    [Fact]
    public async Task CreateRunPlan_SavesItNamedAfterThePlanAndTellsTheUi()
    {
        _format.Parse(Arg.Any<string>()).Returns(new RunPlan(
            "Checkout smoke",
            [WorkspacePath.Parse("requests/users.yaml")],
            new RunLoad(1, 1, null, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, null),
            null,
            new Dictionary<string, RunAssertion>(),
            RunThresholds.None));
        _plans.WriteAsync(Root, Arg.Any<WorkspacePath>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await CallToolAsync("create_run_plan", """{ "folder": "plans/perf", "yaml": "name: Checkout smoke" }""");

        Assert.False(result["isError"]!.GetValue<bool>(), Text(result));
        Assert.Equal("plans/perf/checkout-smoke.run.yaml", JsonNode.Parse(Text(result))!["path"]!.GetValue<string>());
        var message = Assert.Single(_events);
        Assert.Equal("workspace.changed", message["event"]!.GetValue<string>());
        Assert.Equal("plans/perf/checkout-smoke.run.yaml", Assert.Single(message["payload"]!["paths"]!.AsArray())!.GetValue<string>());
    }

    [Fact]
    public async Task CancelRun_OfARunNotInProgress_IsAToolError()
    {
        var result = await CallToolAsync("cancel_run", """{ "runId": "nope" }""");

        Assert.True(result["isError"]!.GetValue<bool>());
        Assert.StartsWith(RunErrorCodes.RunNotFound + ":", Text(result));
    }

    [Fact]
    public void Runs_ShownToTheAssistant_KeepOnlyTheLatestSecondsOfTheTimeline()
    {
        var stats = new StatsDto(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, new Dictionary<string, long>(), new Dictionary<string, long>());
        var timeline = Enumerable.Range(0, McpTools.MaxTimelinePoints + 30).Select(second => new TimelinePointDto(second, 1, 0, 1)).ToList();
        var run = new RunResultDto("r", PlanFile, "Smoke", "completed", DateTimeOffset.UnixEpoch, 0, 1, 0, stats, [], timeline, [], null, [], [], null);

        var shown = McpTools.ForAssistant(run);

        Assert.Equal(McpTools.MaxTimelinePoints, shown.Timeline.Count);
        Assert.Equal(timeline[^1], shown.Timeline[^1]);
    }

    private async Task<JsonNode> CallToolAsync(string name, string arguments)
    {
        var response = await _protocol.HandleAsync(
            $$"""{ "jsonrpc": "2.0", "id": 1, "method": "tools/call", "params": { "name": "{{name}}", "arguments": {{arguments}} } }""");
        Assert.Null(response!["error"]);
        return response["result"]!;
    }

    private static string Text(JsonNode result) => result["content"]![0]!["text"]!.GetValue<string>();
}
