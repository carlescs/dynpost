using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Dynpost.Application.Runs;
using Dynpost.Application.Workspaces;
using Dynpost.Desktop.Bridge;
using NSubstitute;

namespace Dynpost.Desktop.Tests.Bridge;

/// <summary>The run messages, through the real router and use cases, with a mocked run store.</summary>
public sealed class RunBridgeTests
{
    private const string Root = "/work/api";

    private static StatsDto Stats(double p95) => new(
        10, 0, 0, 1, p95, p95, p95, p95, p95, p95, 5, 0, 0,
        new Dictionary<string, long>(), new Dictionary<string, long>());

    private static RunResultDto Run(string runId, double p95) => new(
        runId, "plans/smoke.run.yaml", "Smoke", "completed", DateTimeOffset.UnixEpoch, 2000, 1, 0,
        Stats(p95), [new("requests/users.yaml", Stats(p95))], [], [], null, [], [], null);

    private static async Task<JsonElement> CompareAsync()
    {
        var store = Substitute.For<IRunStore>();
        store.GetAsync(Root, "old", Arg.Any<CancellationToken>()).Returns(Run("old", 100));
        store.GetAsync(Root, "new", Arg.Any<CancellationToken>()).Returns(Run("new", 200));
        var current = new CurrentWorkspace();
        current.Open(Root);
        using var services = new ServiceCollection()
            .AddSingleton(store)
            .AddSingleton(current)
            .AddSingleton<RunRegistry>()
            .AddTransient<GetRunProgress>()
            .AddTransient<CompareRuns>()
            .BuildServiceProvider();
        var router = BridgeHandlers.Create(services, Substitute.For<IFileDialogs>(), Substitute.For<IAppWindow>());

        var json = await router.HandleAsync(JsonSerializer.Serialize(new
        {
            id = "1",
            type = "run.compare",
            payload = new { baselineRunId = "old", runId = "new" },
        }));
        return JsonDocument.Parse(json!).RootElement.Clone();
    }

    [Fact]
    public async Task Compare_ReportsTheDeltas()
    {
        var response = await CompareAsync();

        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        var payload = response.GetProperty("payload");
        Assert.Equal("old", payload.GetProperty("baseline").GetProperty("runId").GetString());
        var p95 = payload.GetProperty("total").GetProperty("metrics").EnumerateArray()
            .Single(m => m.GetProperty("metric").GetString() == "p95");
        Assert.Equal("worse", p95.GetProperty("verdict").GetString());
        Assert.Equal(100, p95.GetProperty("changePercent").GetDouble());
    }

    [Fact]
    public async Task Compare_SendsEveryFieldTheClientReads()
    {
        var payload = (await CompareAsync()).GetProperty("payload");

        // The names of models/messages.ts RunComparisonDto, ComparisonRowDto and MetricDeltaDto.
        static string[] Names(JsonElement element) => element.EnumerateObject().Select(p => p.Name).Order().ToArray();
        Assert.Equal(new[] { "baseline", "current", "environments", "requests", "total" }, Names(payload));
        var row = payload.GetProperty("requests")[0];
        Assert.Equal(new[] { "inBaseline", "inCurrent", "key", "metrics", "name" }, Names(row));
        Assert.Equal(
            new[] { "baseline", "change", "changePercent", "current", "metric", "verdict" },
            Names(row.GetProperty("metrics")[0]));
    }
}
