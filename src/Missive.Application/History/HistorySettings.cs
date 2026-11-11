namespace Missive.Application.History;

/// <summary>Whether responses are kept in the requests' history, and how much of them.</summary>
/// <param name="Enabled">Whether responses are recorded at all.</param>
/// <param name="MaxEntries">The responses kept per request.</param>
/// <param name="MaxBodyBytes">Bodies larger than this are cut off (text) or not kept (binary).</param>
public sealed record HistorySettings(bool Enabled = true, int MaxEntries = HistorySettings.DefaultMaxEntries, long MaxBodyBytes = HistorySettings.DefaultMaxBodyBytes)
{
    public const int DefaultMaxEntries = 20;
    public const long DefaultMaxBodyBytes = 1024 * 1024;
    public const int MaxEntriesLimit = 500;
    public const long MaxBodyBytesLimit = 50L * 1024 * 1024;

    /// <summary>The size all the history of a workspace may take; the oldest entries go first.</summary>
    public const long MaxTotalBytes = 256L * 1024 * 1024;

    public static HistorySettings Default { get; } = new();

    /// <summary>The settings with out-of-range values (from a hand-edited settings file) replaced by the closest valid ones.</summary>
    public HistorySettings Normalized() =>
        this with
        {
            MaxEntries = Math.Clamp(MaxEntries, 1, MaxEntriesLimit),
            MaxBodyBytes = Math.Clamp(MaxBodyBytes, 0, MaxBodyBytesLimit),
        };
}
