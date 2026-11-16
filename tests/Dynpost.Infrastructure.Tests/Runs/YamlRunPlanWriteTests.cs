using Dynpost.Domain.Runs;
using Dynpost.Domain.Workspaces;
using Dynpost.Infrastructure.Runs;

namespace Dynpost.Infrastructure.Tests.Runs;

public class YamlRunPlanWriteTests
{
    private readonly YamlRunPlanFormat _format = new();

    private static WorkspacePath P(string path) => WorkspacePath.Parse(path);

    [Fact]
    public void WritesTheKeysInTheDocumentedOrderWithoutDefaults()
    {
        var plan = new RunPlan(
            "Checkout: load",
            [P("requests/shop/list.yaml"), P("requests/shop/buy.yaml"), P("requests/shop/off.yaml")],
            new RunLoad(10, null, TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(300), 12.5),
            new RunDataSource(P("data/Shop/users.csv"), SelectionMode.PerThread, EndBehavior.Stop),
            new Dictionary<string, RunAssertion>
            {
                ["requests/shop/buy.yaml"] = new(["201"], null, "\"id\"", null),
                [RunPlan.AllRequests] = new(["2xx", "304"], TimeSpan.FromMilliseconds(800), null, "error"),
            },
            RunThresholds.None with { P95 = TimeSpan.FromMilliseconds(500), ErrorRatePercent = 1 },
            RequestOrder.Random,
            new Dictionary<string, double> { ["requests/shop/list.yaml"] = 33.333 },
            ["requests/shop/off.yaml"]);

        var text = _format.Write(plan);

        Assert.Equal(
            """
            name: "Checkout: load"
            requests:
              - requests/shop/list.yaml
              - requests/shop/buy.yaml
              - requests/shop/off.yaml
            disabled:
              - requests/shop/off.yaml
            order: random
            weights:
              requests/shop/list.yaml: 33.333
            load:
              threads: 10
              duration: 2m
              rampUp: 10s
              thinkTime:
                min: 50ms
                max: 300ms
              maxRps: 12.5
            data:
              file: data/Shop/users.csv
              mode: perThread
              onEof: stop
            assert:
              "*":
                status: 2xx, 304
                maxTime: 800ms
                bodyNotContains: error
              requests/shop/buy.yaml:
                status: 201
                bodyContains: "\"id\""
            thresholds:
              p95: 500ms
              errorRate: 1%

            """.Replace("\r\n", "\n"),
            text);

        var parsed = _format.Parse(text);
        Assert.True(parsed.IsSuccess, parsed.Error?.Message);
        var read = parsed.Value;
        Assert.Equal(plan.Name, read.Name);
        Assert.Equal(plan.Requests, read.Requests);
        Assert.Equal(plan.DisabledRequests, read.DisabledRequests);
        Assert.Equal(plan.Load, read.Load);
        Assert.Equal(plan.Data, read.Data);
        Assert.Equal(plan.Order, read.Order);
        Assert.Equal(plan.RequestWeights, read.RequestWeights);
        Assert.Equal(plan.Thresholds, read.Thresholds);
        foreach (var (key, assertion) in plan.Assertions)
        {
            Assert.Equal(assertion.Status, read.Assertions[key].Status);
            Assert.Equal(assertion with { Status = [] }, read.Assertions[key] with { Status = [] });
        }
    }

    [Fact]
    public void WritesAMinimalPlan()
    {
        var plan = new RunPlan(
            "Smoke",
            [P("requests/ping.yaml")],
            new RunLoad(1, 3, null, TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), null),
            null,
            new Dictionary<string, RunAssertion>(),
            RunThresholds.None);

        Assert.Equal(
            "name: Smoke\nrequests:\n  - requests/ping.yaml\nload:\n  threads: 1\n  iterations: 3\n  thinkTime: 1s\n",
            _format.Write(plan));
    }
}
