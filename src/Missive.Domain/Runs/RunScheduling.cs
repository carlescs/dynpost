namespace Missive.Domain.Runs;

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
