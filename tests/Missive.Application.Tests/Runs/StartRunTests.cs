using System.Text;
using Missive.Application.Common;
using Missive.Application.Cookies;
using Missive.Application.Environments;
using Missive.Application.Http;
using Missive.Application.OAuth2;
using Missive.Application.Runs;
using Missive.Application.Workspaces;
using Missive.Domain.Http;
using Missive.Domain.Requests;
using Missive.Domain.Runs;
using Missive.Domain.Workspaces;
using NSubstitute;

namespace Missive.Application.Tests.Runs;

public class StartRunTests
{
    private const string Root = "/work/api";
    private const string PlanFile = "perf/smoke.run.yaml";

    private readonly IRunPlanStore _plans = Substitute.For<IRunPlanStore>();
    private readonly IRunPlanFormat _format = Substitute.For<IRunPlanFormat>();
    private readonly IRunDataReader _data = Substitute.For<IRunDataReader>();
    private readonly IWorkspaceRepository _repository = Substitute.For<IWorkspaceRepository>();
    private readonly IHttpSender _sender = Substitute.For<IHttpSender>();
    private readonly IRunStore _runs = Substitute.For<IRunStore>();
    private readonly RunRegistry _registry = new();
    private readonly CurrentWorkspace _current = new();

    public StartRunTests()
    {
        _current.Open(Root);
        _plans.ReadAsync(Root, WorkspacePath.Parse(PlanFile), Arg.Any<CancellationToken>()).Returns("plan");
        _repository.FileExists(Root, Arg.Any<WorkspacePath>()).Returns(true);
        _repository.ReadRequestAsync(Root, Arg.Any<WorkspacePath>(), Arg.Any<CancellationToken>())
            .Returns(new HttpRequestDefinition("Get", RequestMethod.Get, "https://api.test/users/{{id}}"));
        _runs.SaveAsync(Root, Arg.Any<RunResultDto>(), Arg.Any<CancellationToken>()).Returns(Unit.Value);
    }

    private StartRun Create()
    {
        var state = Substitute.For<IWorkspaceStateStore>();
        state.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(WorkspaceState.Default);
        var time = TimeProvider.System;
        return new StartRun(
            _plans,
            _format,
            _data,
            _repository,
            _sender,
            new ActiveEnvironment(
                Substitute.For<IEnvironmentStore>(),
                Substitute.For<ISecretStore>(),
                Substitute.For<ICapturedValueStore>(),
                state,
                _current),
            new OAuth2Tokens(_sender, Substitute.For<IOAuth2TokenStore>(), Substitute.For<IAuthorizationBrowser>(), time),
            _runs,
            _registry,
            _current,
            time);
    }

    private void UsePlan(RunPlan plan) => _format.Parse("plan").Returns(plan);

    private static RunPlan Plan(
        int threads = 2,
        int iterations = 3,
        RunDataSource? data = null,
        Dictionary<string, RunAssertion>? assertions = null,
        RunThresholds? thresholds = null,
        params string[] requests) => new(
            "Smoke",
            (requests.Length == 0 ? ["users/get.yaml"] : requests).Select(WorkspacePath.Parse).ToList(),
            new RunLoad(threads, iterations, null, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, null),
            data,
            assertions ?? new Dictionary<string, RunAssertion>(),
            thresholds ?? RunThresholds.None);

    private static HttpResponseData Response(int status = 200, string body = "ok") =>
        new(status, "OK", [], Encoding.UTF8.GetBytes(body), false, TimeSpan.FromMilliseconds(5));

    private async Task<RunResultDto> RunToEndAsync(string runId)
    {
        var progress = new GetRunProgress(_registry, _runs, _current);
        for (var i = 0; i < 200; i++)
        {
            var result = (await progress.ExecuteAsync(runId)).Value;
            if (result.State != "running")
            {
                return result;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException("The run didn't finish.");
    }

    [Fact]
    public async Task Execute_SendsEveryRequestOnEveryIterationOfEveryThread()
    {
        UsePlan(Plan(requests: ["users/get.yaml", "users/get.yaml"]));
        _sender.SendAsync(Arg.Any<OutgoingRequest>(), Arg.Any<CancellationToken>()).Returns(Response());
        _repository.ReadRequestAsync(Root, Arg.Any<WorkspacePath>(), Arg.Any<CancellationToken>())
            .Returns(new HttpRequestDefinition("Get", RequestMethod.Get, "https://api.test/users"));

        var started = await Create().ExecuteAsync(PlanFile, false);
        var run = await RunToEndAsync(started.Value.RunId);

        Assert.Equal("completed", run.State);
        Assert.Equal(2 * 3 * 2, run.Total.Samples);
        Assert.Equal(0, run.Total.Errors);
        Assert.Equal(12, run.Requests.Single().Stats.Samples);
        Assert.True(run.Passed);
        await _runs.Received(1).SaveAsync(Root, Arg.Is<RunResultDto>(r => r.RunId == started.Value.RunId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_LaysCsvRowsOverTheVariables()
    {
        UsePlan(Plan(1, 2, new RunDataSource(WorkspacePath.Parse("perf/users.csv"), SelectionMode.Sequential, EndBehavior.Recycle)));
        _data.ReadCsvAsync(Root, Arg.Any<WorkspacePath>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<IReadOnlyDictionary<string, string>>>.Success(
            [
                new Dictionary<string, string> { ["id"] = "7" },
                new Dictionary<string, string> { ["id"] = "8" },
            ]));
        _sender.SendAsync(Arg.Any<OutgoingRequest>(), Arg.Any<CancellationToken>()).Returns(Response());

        var started = await Create().ExecuteAsync(PlanFile, false);
        await RunToEndAsync(started.Value.RunId);

        await _sender.Received(1).SendAsync(Arg.Is<OutgoingRequest>(r => r.Url.AbsolutePath == "/users/7"), Arg.Any<CancellationToken>());
        await _sender.Received(1).SendAsync(Arg.Is<OutgoingRequest>(r => r.Url.AbsolutePath == "/users/8"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_CountsHttpErrorsAndFailedAssertionsAndKeepsThoseSamples()
    {
        var assertions = new Dictionary<string, RunAssertion>
        {
            ["users/get.yaml"] = new(["201"], null, null, null),
        };
        UsePlan(Plan(1, 2, assertions: assertions, thresholds: RunThresholds.None with { ErrorRatePercent = 10 }));
        _repository.ReadRequestAsync(Root, Arg.Any<WorkspacePath>(), Arg.Any<CancellationToken>())
            .Returns(new HttpRequestDefinition("Get", RequestMethod.Get, "https://api.test/users"));
        _sender.SendAsync(Arg.Any<OutgoingRequest>(), Arg.Any<CancellationToken>()).Returns(Response(200, "nope"));

        var run = await RunToEndAsync((await Create().ExecuteAsync(PlanFile, false)).Value.RunId);

        Assert.Equal(2, run.Total.Errors);
        Assert.Equal("Expected status 201, got 200", Assert.Single(run.Total.ErrorReasons.Keys));
        Assert.Equal(2, run.FailedSamples.Count);
        Assert.Equal("nope", run.FailedSamples[0].ResponsePreview);
        Assert.False(run.Passed);
        Assert.False(run.Thresholds.Single().Passed);
    }

    [Fact]
    public async Task Execute_ReportsMissingVariablesAsErrorsWithoutSending()
    {
        UsePlan(Plan(1, 1));

        var run = await RunToEndAsync((await Create().ExecuteAsync(PlanFile, false)).Value.RunId);

        Assert.Equal(1, run.Total.Errors);
        Assert.Contains("id", run.FailedSamples.Single().Error);
        await _sender.DidNotReceiveWithAnyArgs().SendAsync(default!, default);
    }

    [Fact]
    public async Task Execute_RequiresConfirmationForVeryLargeRuns()
    {
        UsePlan(Plan(threads: 500));

        var result = await Create().ExecuteAsync(PlanFile, false);

        Assert.Equal(RunErrorCodes.ConfirmationRequired, result.Error!.Code);
    }

    [Fact]
    public async Task Execute_ReportsPlansThatReferToMissingRequests()
    {
        UsePlan(Plan());
        _repository.FileExists(Root, Arg.Any<WorkspacePath>()).Returns(false);

        var result = await Create().ExecuteAsync(PlanFile, false);

        Assert.Contains("users/get.yaml", result.Error!.Message);
    }

    [Fact]
    public async Task Cancel_StopsTheRunAndKeepsThePartialResults()
    {
        UsePlan(Plan(1, 1_000_000));
        _repository.ReadRequestAsync(Root, Arg.Any<WorkspacePath>(), Arg.Any<CancellationToken>())
            .Returns(new HttpRequestDefinition("Get", RequestMethod.Get, "https://api.test/users"));
        _sender.SendAsync(Arg.Any<OutgoingRequest>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                await Task.Delay(5, call.Arg<CancellationToken>());
                return Result<HttpResponseData>.Success(Response());
            });
        var started = await Create().ExecuteAsync(PlanFile, false);
        var progress = new GetRunProgress(_registry, _runs, _current);
        while ((await progress.ExecuteAsync(started.Value.RunId)).Value.Total.Samples == 0)
        {
            await Task.Delay(10);
        }

        Assert.True(new CancelRun(_registry).Execute(started.Value.RunId).IsSuccess);
        var run = await RunToEndAsync(started.Value.RunId);

        Assert.Equal("cancelled", run.State);
        Assert.True(run.Total.Samples > 0);
        Assert.Null(run.Passed);
    }

    [Fact]
    public async Task Execute_RejectsAPlanThatIsAlreadyRunning()
    {
        UsePlan(Plan(1, 1_000_000));
        _repository.ReadRequestAsync(Root, Arg.Any<WorkspacePath>(), Arg.Any<CancellationToken>())
            .Returns(new HttpRequestDefinition("Get", RequestMethod.Get, "https://api.test/users"));
        _sender.SendAsync(Arg.Any<OutgoingRequest>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                await Task.Delay(5, call.Arg<CancellationToken>());
                return Result<HttpResponseData>.Success(Response());
            });
        var started = await Create().ExecuteAsync(PlanFile, false);

        var second = await Create().ExecuteAsync(PlanFile, false);
        new CancelRun(_registry).Execute(started.Value.RunId);

        Assert.Equal(ErrorCodes.AlreadyExists, second.Error!.Code);
    }
}
