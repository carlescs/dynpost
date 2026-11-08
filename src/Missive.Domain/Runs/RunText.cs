using System.Globalization;

namespace Missive.Domain.Runs;

/// <summary>Reading and writing the short forms used in run plans: <c>500ms</c>, <c>10s</c>, <c>2m</c>, <c>1h</c>, <c>1%</c>.</summary>
public static class RunText
{
    public static bool TryParseDuration(string? text, out TimeSpan value)
    {
        value = TimeSpan.Zero;
        var trimmed = text?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(trimmed))
        {
            return false;
        }

        var (suffix, scale) = trimmed switch
        {
            _ when trimmed.EndsWith("ms", StringComparison.Ordinal) => ("ms", 1.0),
            _ when trimmed.EndsWith('s') => ("s", 1000.0),
            _ when trimmed.EndsWith('m') => ("m", 60_000.0),
            _ when trimmed.EndsWith('h') => ("h", 3_600_000.0),
            _ => ("", 1000.0),
        };

        var number = trimmed[..^suffix.Length];
        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount)
            || amount < 0
            || !double.IsFinite(amount)
            || amount * scale > TimeSpan.MaxValue.TotalMilliseconds)
        {
            return false;
        }

        value = TimeSpan.FromMilliseconds(amount * scale);
        return true;
    }

    public static string FormatDuration(TimeSpan value)
    {
        var ms = value.TotalMilliseconds;
        if (ms % 3_600_000 == 0 && ms > 0)
        {
            return $"{ms / 3_600_000:0}h";
        }

        if (ms % 60_000 == 0 && ms > 0)
        {
            return $"{ms / 60_000:0}m";
        }

        return ms % 1000 == 0 ? $"{ms / 1000:0}s" : string.Create(CultureInfo.InvariantCulture, $"{ms:0.###}ms");
    }

    /// <summary>A percentage such as <c>1%</c> or <c>0.5</c>.</summary>
    public static bool TryParsePercent(string? text, out double value)
    {
        value = 0;
        var trimmed = text?.Trim().TrimEnd('%').Trim();
        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && value is >= 0 and <= 100;
    }

    /// <summary>Whether a status code matches <c>201</c> or <c>2xx</c>.</summary>
    public static bool StatusMatches(string pattern, int statusCode)
    {
        var trimmed = pattern.Trim();
        if (trimmed.Length == 3 && char.IsDigit(trimmed[0]) && trimmed[1..].Equals("xx", StringComparison.OrdinalIgnoreCase))
        {
            return statusCode / 100 == trimmed[0] - '0';
        }

        return int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var code) && code == statusCode;
    }

    public static bool IsValidStatusPattern(string pattern)
    {
        var trimmed = pattern.Trim();
        return trimmed.Length == 3
            && (trimmed.All(char.IsAsciiDigit)
                || (char.IsAsciiDigit(trimmed[0]) && trimmed[1..].Equals("xx", StringComparison.OrdinalIgnoreCase)));
    }
}
