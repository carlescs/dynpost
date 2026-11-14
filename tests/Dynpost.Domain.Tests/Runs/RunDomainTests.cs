using Dynpost.Domain.Requests;
using Dynpost.Domain.Runs;
using Dynpost.Domain.Workspaces;

namespace Dynpost.Domain.Tests.Runs;

public class RunDomainTests
{
    [Theory]
    [InlineData("500ms", 500)]
    [InlineData("10s", 10_000)]
    [InlineData("2m", 120_000)]
    [InlineData("1h", 3_600_000)]
    [InlineData("1.5s", 1500)]
    [InlineData("3", 3000)]
    public void ParseDuration_ReadsTheShortForms(string text, double milliseconds)
    {
        Assert.True(RunText.TryParseDuration(text, out var value));
        Assert.Equal(milliseconds, value.TotalMilliseconds);
    }

    [Theory]
    [InlineData("")]
    [InlineData("fast")]
    [InlineData("-1s")]
    [InlineData("10x")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void ParseDuration_RejectsOtherText(string text) => Assert.False(RunText.TryParseDuration(text, out _));

    [Theory]
    [InlineData(500, "500ms")]
    [InlineData(10_000, "10s")]
    [InlineData(120_000, "2m")]
    [InlineData(3_600_000, "1h")]
    public void FormatDuration_UsesTheLargestExactUnit(double milliseconds, string expected) =>
        Assert.Equal(expected, RunText.FormatDuration(TimeSpan.FromMilliseconds(milliseconds)));

    [Theory]
    [InlineData("201", 201, true)]
    [InlineData("201", 200, false)]
    [InlineData("2xx", 204, true)]
    [InlineData("2XX", 301, false)]
    public void StatusMatches_AcceptsCodesAndClasses(string pattern, int status, bool expected) =>
        Assert.Equal(expected, RunText.StatusMatches(pattern, status));

    [Fact]
    public void Histogram_ReportsPercentiles()
    {
        var histogram = new LatencyHistogram();
        for (var i = 1; i <= 100; i++)
        {
            histogram.Record(i);
        }

        Assert.Equal(50, histogram.Percentile(50));
        Assert.Equal(95, histogram.Percentile(95));
        Assert.Equal(100, histogram.Percentile(100));
    }

    [Fact]
    public void Histogram_KeepsLargeValuesWithinAFewPercent()
    {
        var histogram = new LatencyHistogram();
        histogram.Record(5000);

        Assert.InRange(histogram.Percentile(50), 5000, 5000 * 1.04);
    }

    [Fact]
    public void Stats_SummarizeSamplesAndErrors()
    {
        var stats = new StatsAccumulator();
        stats.Add(10, 200, null, 5, 100);
        stats.Add(30, 500, "HTTP 500", 5, 50);

        var snapshot = stats.Snapshot(TimeSpan.FromSeconds(2));

        Assert.Equal(2, snapshot.Samples);
        Assert.Equal(1, snapshot.Errors);
        Assert.Equal(50, snapshot.ErrorPercent);
        Assert.Equal(10, snapshot.Min);
        Assert.Equal(20, snapshot.Average);
        Assert.Equal(30, snapshot.Max);
        Assert.Equal(1, snapshot.ThroughputPerSecond);
        Assert.Equal(150, snapshot.BytesReceived);
        Assert.Equal(1, snapshot.StatusCodes["500"]);
        Assert.Equal(1, snapshot.ErrorReasons["HTTP 500"]);
    }

    [Fact]
    public void Thresholds_FailWhenALimitIsExceeded()
    {
        var stats = new StatsAccumulator();
        stats.Add(100, 200, null, 0, 0);
        var total = stats.Snapshot(TimeSpan.FromSeconds(1));

        var outcomes = ThresholdEvaluator.Evaluate(RunThresholds.None with { P95 = TimeSpan.FromMilliseconds(50), ErrorRatePercent = 1 }, total);

        Assert.False(outcomes.Single(o => o.Name == "p95").Passed);
        Assert.True(outcomes.Single(o => o.Name == "errorRate").Passed);
    }

    [Fact]
    public void StartDelay_SpreadsThreadsEvenlyOverTheRampUp()
    {
        var delays = Enumerable.Range(0, 4).Select(t => RunScheduling.StartDelay(t, 4, TimeSpan.FromSeconds(8))).ToList();

        Assert.Equal([0, 2, 4, 6], delays.Select(d => (int)d.TotalSeconds));
    }

    [Fact]
    public void RateLimiter_SpacesRequests()
    {
        var limiter = new RateLimiter(10);

        var first = limiter.Reserve(0);
        var second = limiter.Reserve(0);
        var third = limiter.Reserve(0);

        Assert.Equal(TimeSpan.Zero, first);
        Assert.Equal(TimeSpan.FromMilliseconds(100), second);
        Assert.Equal(TimeSpan.FromMilliseconds(200), third);
    }

    [Fact]
    public void Cursor_Sequential_SharesOneCursorAndRecyclesOrStops()
    {
        var recycle = new DataCursor(2, SelectionMode.Sequential, EndBehavior.Recycle, 2);
        Assert.Equal([0, 1, 0, 1], new[] { recycle.Next(0), recycle.Next(1), recycle.Next(0), recycle.Next(1) });

        var stop = new DataCursor(2, SelectionMode.Sequential, EndBehavior.Stop, 2);
        Assert.Equal(0, stop.Next(0));
        Assert.Equal(1, stop.Next(1));
        Assert.Null(stop.Next(0));
    }

    [Fact]
    public void Cursor_PerThread_DealsEntriesOutInTurns()
    {
        var cursor = new DataCursor(5, SelectionMode.PerThread, EndBehavior.Stop, 2);

        Assert.Equal([0, 2, 4], new[] { cursor.Next(0), cursor.Next(0), cursor.Next(0) });
        Assert.Null(cursor.Next(0));
        Assert.Equal([1, 3], new[] { cursor.Next(1), cursor.Next(1) });
        Assert.Null(cursor.Next(1));
    }

    [Fact]
    public void Cursor_PerThread_RecyclesToTheThreadsFirstEntry()
    {
        var cursor = new DataCursor(2, SelectionMode.PerThread, EndBehavior.Recycle, 2);

        Assert.Equal([1, 1, 1], new[] { cursor.Next(1), cursor.Next(1), cursor.Next(1) });
    }

    [Fact]
    public void Cursor_Random_StaysInRange()
    {
        var cursor = new DataCursor(3, SelectionMode.Random, EndBehavior.Recycle, 1);

        Assert.All(Enumerable.Range(0, 50), _ => Assert.InRange(cursor.Next(0)!.Value, 0, 2));
    }

    private static RunPlan Plan(RunLoad? load = null) => new(
        "Smoke",
        [WorkspacePath.Parse("a.yaml")],
        load ?? new RunLoad(2, 5, null, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, null),
        null,
        new Dictionary<string, RunAssertion>(),
        RunThresholds.None);

    [Fact]
    public void Validate_AcceptsAGoodPlan() => Assert.Empty(Plan().Validate());

    [Theory]
    [InlineData("environments/load.yaml", true)]
    [InlineData("environments/nested/load.yaml", false)]
    [InlineData("requests/load.yaml", false)]
    [InlineData("environments/load.txt", false)]
    public void Validate_ChecksTheEnvironmentsAreEnvironmentFiles(string environment, bool valid)
    {
        var errors = (Plan() with { Environments = [WorkspacePath.Parse("environments/dev.yaml"), WorkspacePath.Parse(environment)] }).Validate();

        Assert.Equal(valid, errors.Count == 0);
        Assert.All(errors, error => Assert.Contains("'environments'", error));
    }

    private static RunPlan EnvironmentPlan(RequestOrder order, Dictionary<string, double>? weights = null) => OrderPlan(RequestOrder.Sequential) with
    {
        Environments = new[] { "environments/dev.yaml", "environments/qa.yaml", "environments/load.yaml" }.Select(WorkspacePath.Parse).ToList(),
        EnvironmentOrder = order,
        EnvironmentWeights = weights,
    };

    [Fact]
    public void EnvironmentPicker_FollowsTheEnvironmentOrder()
    {
        var random = new Random(1);

        Assert.Equal(
            ["environments/dev.yaml", "environments/qa.yaml", "environments/load.yaml"],
            Names(new EnvironmentPicker(EnvironmentPlan(RequestOrder.Sequential)).ForIteration(0, 0, random)));
        var roundRobin = new EnvironmentPicker(EnvironmentPlan(RequestOrder.RoundRobin));
        Assert.Equal("environments/qa.yaml", Assert.Single(roundRobin.ForIteration(0, 1, random)).Value);
        Assert.Equal("environments/load.yaml", Assert.Single(roundRobin.ForIteration(1, 1, random)).Value);
        Assert.Empty(new EnvironmentPicker(OrderPlan(RequestOrder.Sequential)).ForIteration(0, 0, random));
    }

    [Fact]
    public void EnvironmentPicker_Random_FollowsTheWeights()
    {
        var picker = new EnvironmentPicker(EnvironmentPlan(RequestOrder.Random, new() { ["environments/dev.yaml"] = 0, ["environments/load.yaml"] = 3 }));
        var random = new Random(7);
        var counts = new Dictionary<string, int>();
        for (var i = 0; i < 4000; i++)
        {
            var name = Assert.Single(picker.ForIteration(0, i, random)).Value;
            counts[name] = counts.GetValueOrDefault(name) + 1;
        }

        Assert.False(counts.ContainsKey("environments/dev.yaml"));
        Assert.InRange(counts["environments/load.yaml"] / (double)counts["environments/qa.yaml"], 2.5, 3.5);
    }

    [Fact]
    public void RequestsPerIteration_CountsEachEnvironmentOfTheIteration()
    {
        Assert.Equal(9, EnvironmentPlan(RequestOrder.Sequential).RequestsPerIteration);
        Assert.Equal(9, EnvironmentPlan(RequestOrder.Shuffle).RequestsPerIteration);
        Assert.Equal(3, EnvironmentPlan(RequestOrder.Random).RequestsPerIteration);
        Assert.Equal(3, EnvironmentPlan(RequestOrder.RoundRobin).RequestsPerIteration);
    }

    [Fact]
    public void Validate_RejectsBadEnvironmentWeights()
    {
        Assert.Contains("environments/gone.yaml", Assert.Single(EnvironmentPlan(RequestOrder.Random, new() { ["environments/gone.yaml"] = 1 }).Validate()));
        Assert.Contains("zero or more", Assert.Single(EnvironmentPlan(RequestOrder.Random, new() { ["environments/dev.yaml"] = -1 }).Validate()));
        var allZero = EnvironmentPlan(RequestOrder.Random, new() { ["environments/dev.yaml"] = 0, ["environments/qa.yaml"] = 0, ["environments/load.yaml"] = 0 });
        Assert.Contains("environment needs a weight", Assert.Single(allZero.Validate()));
    }

    [Fact]
    public void Validate_RejectsIterationsAndDurationTogether()
    {
        var load = new RunLoad(2, 5, TimeSpan.FromSeconds(10), TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, null);

        Assert.Contains("not both", Assert.Single(Plan(load).Validate()));
    }

    [Fact]
    public void Validate_RejectsTooManyThreadsAndMissingLength()
    {
        var load = new RunLoad(RunPlan.MaxThreads + 1, null, null, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, null);

        Assert.Equal(2, Plan(load).Validate().Count);
    }

    [Fact]
    public void Validate_AcceptsTheMaximumThreads()
    {
        var load = new RunLoad(RunPlan.MaxThreads, 1, null, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, null);

        Assert.Empty(Plan(load).Validate());
    }

    [Fact]
    public void Validate_RejectsAssertionsForUnlistedRequests()
    {
        var plan = Plan() with { Assertions = new Dictionary<string, RunAssertion> { ["b.yaml"] = new([], null, null, null) } };

        Assert.Contains("b.yaml", Assert.Single(plan.Validate()));
    }

    [Fact]
    public void Validate_RejectsBodyFoldersForUnlistedRequests()
    {
        var plan = Plan() with
        {
            Bodies = new Dictionary<string, RunBodySource>
            {
                ["a.yaml"] = new(WorkspacePath.Parse("bodies"), null, SelectionMode.Sequential, EndBehavior.Recycle),
                ["b.yaml"] = new(WorkspacePath.Parse("bodies"), null, SelectionMode.Sequential, EndBehavior.Recycle),
            },
        };

        Assert.Contains("'bodies' refers to 'b.yaml'", Assert.Single(plan.Validate()));
    }

    [Fact]
    public void WithoutDisabledRequests_DropsTheBodyFoldersOfDisabledRequests()
    {
        var source = new RunBodySource(WorkspacePath.Parse("bodies"), null, SelectionMode.Sequential, EndBehavior.Recycle);
        var plan = Plan() with
        {
            Requests = new[] { "a.yaml", "b.yaml" }.Select(WorkspacePath.Parse).ToList(),
            Disabled = ["b.yaml"],
            Bodies = new Dictionary<string, RunBodySource> { ["a.yaml"] = source, ["b.yaml"] = source },
        };

        Assert.Equal(["a.yaml"], plan.WithoutDisabledRequests().BodySources.Keys);
    }

    [Fact]
    public void Bodies_ReplaceTextContentAndKeepTheBodyType()
    {
        var file = new RunBodyFile(WorkspacePath.Parse("bodies/1.json"), "{\"id\":1}");

        Assert.Equal(new RequestBody(BodyType.Json, "{\"id\":1}"), RunBodies.Replace(new RequestBody(BodyType.Json, "{}"), file));
        Assert.Equal(
            new RequestBody(BodyType.Graphql, "{\"id\":1}", "{\"v\":1}"),
            RunBodies.Replace(new RequestBody(BodyType.Graphql, "query", "{\"v\":1}"), file));
        Assert.Equal(
            new RequestBody(BodyType.UrlEncoded, "{\"id\":1}"),
            RunBodies.Replace(RequestBody.WithFields(BodyType.UrlEncoded, [new FormField("a", "1")]), file));
    }

    [Fact]
    public void Bodies_SendBinaryFilesByPath()
    {
        var file = new RunBodyFile(WorkspacePath.Parse("bodies/1.bin"), null);

        Assert.Equal("bodies/1.bin", RunBodies.Replace(new RequestBody(BodyType.Binary, "old.bin"), file).Content);
        Assert.False(RunBodies.UsesText(BodyType.Binary));
        Assert.True(RunBodies.UsesText(BodyType.UrlEncoded));
    }

    [Theory]
    [InlineData(BodyType.None, true)]
    [InlineData(BodyType.Form, true)]
    [InlineData(BodyType.Json, false)]
    [InlineData(BodyType.Text, false)]
    [InlineData(BodyType.Graphql, false)]
    [InlineData(BodyType.UrlEncoded, false)]
    [InlineData(BodyType.Binary, false)]
    public void Bodies_SupportTextAndBinaryBodyTypes(BodyType type, bool unsupported) =>
        Assert.Equal(unsupported, RunBodies.Unsupported(type) is not null);

    private static RunPlan OrderPlan(RequestOrder order, Dictionary<string, double>? weights = null) => Plan() with
    {
        Requests = new[] { "a.yaml", "b.yaml", "c.yaml" }.Select(WorkspacePath.Parse).ToList(),
        Order = order,
        Weights = weights,
    };

    private static string[] Names(IReadOnlyList<WorkspacePath> requests) => requests.Select(r => r.Value).ToArray();

    [Fact]
    public void Picker_Sequential_SendsEveryRequestInOrder()
    {
        var picker = new RequestPicker(OrderPlan(RequestOrder.Sequential));

        Assert.Equal(["a.yaml", "b.yaml", "c.yaml"], Names(picker.ForIteration(1, 4, new Random(1))));
    }

    [Fact]
    public void Picker_Shuffle_SendsEveryRequestOnceInVaryingOrders()
    {
        var picker = new RequestPicker(OrderPlan(RequestOrder.Shuffle));
        var random = new Random(3);
        var orders = new HashSet<string>();
        for (var i = 0; i < 50; i++)
        {
            var names = Names(picker.ForIteration(0, i, random));
            Assert.Equal(["a.yaml", "b.yaml", "c.yaml"], names.Order());
            orders.Add(string.Join(",", names));
        }

        Assert.True(orders.Count > 1);
    }

    [Fact]
    public void Picker_RoundRobin_TakesTheNextRequestAndStaggersThreads()
    {
        var picker = new RequestPicker(OrderPlan(RequestOrder.RoundRobin));
        var random = new Random(1);

        Assert.Equal(["a.yaml", "b.yaml", "c.yaml", "a.yaml"], Enumerable.Range(0, 4).Select(i => Assert.Single(picker.ForIteration(0, i, random)).Value));
        Assert.Equal("b.yaml", Assert.Single(picker.ForIteration(1, 0, random)).Value);
    }

    [Fact]
    public void Picker_Random_FollowsTheWeights()
    {
        var picker = new RequestPicker(OrderPlan(RequestOrder.Random, new() { ["a.yaml"] = 3, ["c.yaml"] = 0 }));
        var random = new Random(7);
        var counts = new Dictionary<string, int>();
        for (var i = 0; i < 4000; i++)
        {
            var name = Assert.Single(picker.ForIteration(0, i, random)).Value;
            counts[name] = counts.GetValueOrDefault(name) + 1;
        }

        Assert.False(counts.ContainsKey("c.yaml"));
        Assert.InRange(counts["a.yaml"] / (double)counts["b.yaml"], 2.5, 3.5);
    }

    [Fact]
    public void RequestsPerIteration_IsOneWhenPickingASingleRequest()
    {
        Assert.Equal(3, OrderPlan(RequestOrder.Shuffle).RequestsPerIteration);
        Assert.Equal(1, OrderPlan(RequestOrder.Random).RequestsPerIteration);
        Assert.Equal(1, OrderPlan(RequestOrder.RoundRobin).RequestsPerIteration);
    }

    [Fact]
    public void Validate_RejectsBadWeights()
    {
        Assert.Contains("d.yaml", Assert.Single(OrderPlan(RequestOrder.Random, new() { ["d.yaml"] = 1 }).Validate()));
        Assert.Contains("zero or more", Assert.Single(OrderPlan(RequestOrder.Random, new() { ["a.yaml"] = -1 }).Validate()));
        var allZero = OrderPlan(RequestOrder.Random, new() { ["a.yaml"] = 0, ["b.yaml"] = 0, ["c.yaml"] = 0 });
        Assert.Contains("greater than zero", Assert.Single(allZero.Validate()));
    }

    [Fact]
    public void DisabledRequests_AreLeftOutOfTheRun()
    {
        var plan = OrderPlan(RequestOrder.Random, new() { ["a.yaml"] = 2, ["b.yaml"] = 3 }) with
        {
            Assertions = new Dictionary<string, RunAssertion>
            {
                ["*"] = new(["2xx"], null, null, null),
                ["b.yaml"] = new(["201"], null, null, null),
            },
            Disabled = ["b.yaml"],
        };

        Assert.Empty(plan.Validate());
        Assert.True(plan.IsDisabled(WorkspacePath.Parse("b.yaml")));
        var running = plan.WithoutDisabledRequests();
        Assert.Equal(["a.yaml", "c.yaml"], Names(running.Requests));
        Assert.Equal(["*"], running.Assertions.Keys);
        Assert.Equal(["a.yaml"], running.RequestWeights.Keys);
        Assert.Empty(running.DisabledRequests);
        Assert.Equal(2, (OrderPlan(RequestOrder.Sequential) with { Disabled = ["b.yaml"] }).RequestsPerIteration);
    }

    [Fact]
    public void Validate_RejectsDisablingEveryRequestOrUnlistedOnes()
    {
        var all = OrderPlan(RequestOrder.Sequential) with { Disabled = ["a.yaml", "b.yaml", "c.yaml"] };
        Assert.Contains("Enable at least one", Assert.Single(all.Validate()));

        var unlisted = OrderPlan(RequestOrder.Sequential) with { Disabled = ["d.yaml"] };
        Assert.Contains("d.yaml", Assert.Single(unlisted.Validate()));

        var onlyWeightedDisabled = OrderPlan(RequestOrder.Random, new() { ["b.yaml"] = 0, ["c.yaml"] = 0 }) with { Disabled = ["a.yaml"] };
        Assert.Contains("greater than zero", Assert.Single(onlyWeightedDisabled.Validate()));
    }
}
