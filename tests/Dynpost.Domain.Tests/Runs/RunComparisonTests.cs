using Dynpost.Domain.Runs;

namespace Dynpost.Domain.Tests.Runs;

public class RunComparisonTests
{
    [Fact]
    public void LowerLatency_IsBetter()
    {
        var delta = RunComparison.Compare(200, 150, MetricDirection.LowerIsBetter, MetricTolerance.Latency);

        Assert.Equal(-50, delta.Change);
        Assert.Equal(-25, delta.ChangePercent);
        Assert.Equal(DeltaVerdict.Better, delta.Verdict);
    }

    [Fact]
    public void HigherLatency_IsWorse() =>
        Assert.Equal(DeltaVerdict.Worse, RunComparison.Compare(100, 120, MetricDirection.LowerIsBetter, MetricTolerance.Latency).Verdict);

    [Fact]
    public void HigherThroughput_IsBetter() =>
        Assert.Equal(DeltaVerdict.Better, RunComparison.Compare(50, 60, MetricDirection.HigherIsBetter, MetricTolerance.Throughput).Verdict);

    [Theory]
    [InlineData(100, 104)] // under 5%
    [InlineData(2, 2.4)] // 20%, but under half a millisecond
    public void SmallChanges_AreTheSame(double baseline, double current) =>
        Assert.Equal(DeltaVerdict.Same, RunComparison.Compare(baseline, current, MetricDirection.LowerIsBetter, MetricTolerance.Latency).Verdict);

    [Fact]
    public void ErrorRate_IsJudgedInPoints()
    {
        var delta = RunComparison.Compare(0, 1, MetricDirection.LowerIsBetter, MetricTolerance.ErrorRate);

        Assert.Null(delta.ChangePercent);
        Assert.Equal(DeltaVerdict.Worse, delta.Verdict);
        Assert.Equal(DeltaVerdict.Same, RunComparison.Compare(10, 10.05, MetricDirection.LowerIsBetter, MetricTolerance.ErrorRate).Verdict);
    }

    [Fact]
    public void BothZero_IsNoChange()
    {
        var delta = RunComparison.Compare(0, 0, MetricDirection.LowerIsBetter, MetricTolerance.ErrorRate);

        Assert.Equal(0, delta.ChangePercent);
        Assert.Equal(DeltaVerdict.Same, delta.Verdict);
    }

    [Fact]
    public void NeutralMetrics_AreNeverBetterOrWorse() =>
        Assert.Equal(DeltaVerdict.Same, RunComparison.Compare(100, 500, MetricDirection.Neutral, MetricTolerance.Exact).Verdict);
}
