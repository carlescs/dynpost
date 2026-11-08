namespace Missive.Domain.Runs;

/// <summary>
/// A fixed-size latency histogram: exact up to 1 s of 1 ms buckets, then logarithmic buckets
/// (about 3% error), so memory stays bounded however long a run lasts. Not thread-safe.
/// </summary>
public sealed class LatencyHistogram
{
    private const int LinearLimit = 1024;
    private const int SubBuckets = 32;
    private const int Octaves = 24;

    private readonly long[] _counts = new long[LinearLimit + (Octaves * SubBuckets)];

    public long Count { get; private set; }

    public void Record(double milliseconds)
    {
        _counts[Index(Math.Max(0, milliseconds))]++;
        Count++;
    }

    /// <summary>The latency below which <paramref name="percentile"/> percent (0-100) of the samples fall.</summary>
    public double Percentile(double percentile)
    {
        if (Count == 0)
        {
            return 0;
        }

        var rank = Math.Max(1, (long)Math.Ceiling(percentile / 100 * Count));
        long seen = 0;
        for (var i = 0; i < _counts.Length; i++)
        {
            seen += _counts[i];
            if (seen >= rank)
            {
                return UpperBound(i);
            }
        }

        return UpperBound(_counts.Length - 1);
    }

    private static int Index(double ms)
    {
        if (ms < LinearLimit)
        {
            return (int)ms;
        }

        var octave = (int)Math.Floor(Math.Log2(ms / LinearLimit));
        if (octave >= Octaves)
        {
            return _lastIndex;
        }

        var start = LinearLimit * Math.Pow(2, octave);
        var sub = (int)((ms - start) / start * SubBuckets);
        return LinearLimit + (octave * SubBuckets) + Math.Min(sub, SubBuckets - 1);
    }

    private static readonly int _lastIndex = LinearLimit + (Octaves * SubBuckets) - 1;

    private static double UpperBound(int index)
    {
        if (index < LinearLimit)
        {
            return index;
        }

        var offset = index - LinearLimit;
        var octave = offset / SubBuckets;
        var sub = offset % SubBuckets;
        var start = LinearLimit * Math.Pow(2, octave);
        return start + ((sub + 1) * start / SubBuckets);
    }
}
