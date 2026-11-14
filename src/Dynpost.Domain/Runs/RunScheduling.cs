using Dynpost.Domain.Workspaces;

namespace Dynpost.Domain.Runs;

/// <summary>Load-schedule maths: when threads start and how requests are paced.</summary>
public static class RunScheduling
{
    /// <summary>When a thread starts, so that all threads start evenly over the ramp-up period.</summary>
    public static TimeSpan StartDelay(int thread, int threads, TimeSpan rampUp) =>
        threads <= 1 ? TimeSpan.Zero : rampUp * ((double)thread / threads);

    /// <summary>A pause between two requests of a thread, picked in the think-time range.</summary>
    public static TimeSpan ThinkTime(RunLoad load, Random random) =>
        load.ThinkMax <= load.ThinkMin
            ? load.ThinkMin
            : load.ThinkMin + ((load.ThinkMax - load.ThinkMin) * random.NextDouble());
}

/// <summary>
/// Spaces requests so that at most <c>maxRps</c> start per second across all threads: each caller
/// reserves the next free slot and waits for it. Thread-safe.
/// </summary>
public sealed class RateLimiter(double maxRps)
{
    private readonly object _gate = new();
    private readonly double _intervalTicks = TimeSpan.TicksPerSecond / maxRps;
    private double _next;

    /// <param name="nowTicks">The current time in ticks of any monotonic clock.</param>
    /// <returns>How long the caller has to wait before sending.</returns>
    public TimeSpan Reserve(long nowTicks)
    {
        lock (_gate)
        {
            var slot = Math.Max(_next, nowTicks);
            _next = slot + _intervalTicks;
            return TimeSpan.FromTicks((long)(slot - nowTicks));
        }
    }
}

/// <summary>
/// Hands out the entries of a data source (CSV rows) to the threads of a run. Thread-safe.
/// <see cref="SelectionMode.PerThread"/> deals the entries out in turns: thread <c>t</c> of <c>n</c>
/// uses entries <c>t</c>, <c>t + n</c>, <c>t + 2n</c>, ….
/// </summary>
public sealed class DataCursor(int count, SelectionMode mode, EndBehavior onEof, int threads, int seed = 0)
{
    private readonly object _gate = new();
    private readonly Random _random = new(seed);
    private readonly int[] _perThread = new int[Math.Max(1, threads)];
    private int _shared;

    /// <summary>The index of the next entry for a thread, or <c>null</c> when the thread should stop.</summary>
    public int? Next(int thread)
    {
        if (count == 0)
        {
            return null;
        }

        lock (_gate)
        {
            switch (mode)
            {
                case SelectionMode.Random:
                    return _random.Next(count);
                case SelectionMode.Sequential:
                    return Advance(_shared, 1, out _shared);
                default:
                    var step = _perThread.Length;
                    var position = _perThread[thread % step];
                    var index = thread % step + (position * step);
                    if (index >= count)
                    {
                        if (onEof == EndBehavior.Stop)
                        {
                            return null;
                        }

                        // Wrap to this thread's first entry, or to entry 0 when it has none.
                        _perThread[thread % step] = 1;
                        return thread % step < count ? thread % step : 0;
                    }

                    _perThread[thread % step] = position + 1;
                    return index;
            }
        }
    }

    private int? Advance(int current, int step, out int next)
    {
        if (current >= count)
        {
            if (onEof == EndBehavior.Stop)
            {
                next = current;
                return null;
            }

            current = 0;
        }

        next = current + step;
        return current;
    }
}

/// <summary>
/// Picks the requests a virtual user sends in one iteration, following the plan's
/// <see cref="RunPlan.Order"/>. Holds no mutable state, so threads can share it as long as each
/// passes its own <see cref="Random"/>.
/// </summary>
public sealed class RequestPicker(RunPlan plan)
{
    private readonly OrderPicker<WorkspacePath> _picker = new(plan.Requests, plan.Order, plan.WeightOf);

    /// <summary>The requests to send, in order, in iteration <paramref name="iteration"/> of a thread.</summary>
    public IReadOnlyList<WorkspacePath> ForIteration(int thread, int iteration, Random random) =>
        _picker.ForIteration(thread, iteration, random);
}

/// <summary>
/// Picks the environments a virtual user sends an iteration's requests with, following the plan's
/// <see cref="RunPlan.EnvironmentOrder"/>, like <see cref="RequestPicker"/> picks requests.
/// </summary>
public sealed class EnvironmentPicker(RunPlan plan)
{
    private readonly OrderPicker<WorkspacePath> _picker = new(plan.EnvironmentList, plan.EnvironmentOrder, plan.EnvironmentWeightOf);

    /// <summary>
    /// The environments of iteration <paramref name="iteration"/> of a thread, in order; empty
    /// when the plan lists none (the selected environment is used).
    /// </summary>
    public IReadOnlyList<WorkspacePath> ForIteration(int thread, int iteration, Random random) =>
        _picker.ForIteration(thread, iteration, random);
}

/// <summary>
/// Picks items for one iteration with a <see cref="RequestOrder"/>: all in order, all shuffled,
/// one at random by weight, or the next one (round robin, each thread starting at a different one).
/// Holds no mutable state.
/// </summary>
public sealed class OrderPicker<T>
{
    private readonly IReadOnlyList<T> _items;
    private readonly RequestOrder _order;
    private readonly double[] _cumulative;

    public OrderPicker(IReadOnlyList<T> items, RequestOrder order, Func<T, double> weight)
    {
        _items = items;
        _order = order;
        _cumulative = new double[items.Count];
        var total = 0d;
        for (var i = 0; i < items.Count; i++)
        {
            total += Math.Max(0, weight(items[i]));
            _cumulative[i] = total;
        }
    }

    /// <summary>The items of iteration <paramref name="iteration"/> of a thread, in order.</summary>
    public IReadOnlyList<T> ForIteration(int thread, int iteration, Random random)
    {
        if (_items.Count == 0)
        {
            return [];
        }

        switch (_order)
        {
            case RequestOrder.Shuffle:
                var shuffled = _items.ToArray();
                random.Shuffle(shuffled);
                return shuffled;
            case RequestOrder.Random:
                return [_items[PickWeighted(random)]];
            case RequestOrder.RoundRobin:
                return [_items[(int)(((long)thread + iteration) % _items.Count)]];
            default:
                return _items;
        }
    }

    private int PickWeighted(Random random)
    {
        var total = _cumulative[^1];
        if (total <= 0)
        {
            return random.Next(_cumulative.Length);
        }

        var target = random.NextDouble() * total;
        for (var i = 0; i < _cumulative.Length; i++)
        {
            if (target < _cumulative[i])
            {
                return i;
            }
        }

        // Rounding can leave the target at the very end: take the last item that has a weight.
        return Array.FindIndex(_cumulative, value => value >= total);
    }
}
