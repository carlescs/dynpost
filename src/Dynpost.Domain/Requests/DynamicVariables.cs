using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Dynpost.Domain.Requests;

/// <summary>A built-in <c>{{$name}}</c> variable, for help texts and hovers.</summary>
public sealed record DynamicVariableInfo(string Name, string Description);

/// <summary>
/// Built-in, <c>$</c>-prefixed variables generated when a request is sent, such as
/// <c>{{$uuid}}</c> or <c>{{$randomInt 1 100}}</c>. Each reference (name and arguments) gets one
/// value per send, so a request that uses <c>{{$uuid}}</c> twice sends the same id twice.
/// </summary>
public static class DynamicVariables
{
    public const char Prefix = '$';

    private const int DefaultRandomMax = 1000;

    /// <summary>The supported variables, in the order they are documented.</summary>
    public static IReadOnlyList<DynamicVariableInfo> All { get; } =
    [
        new("$uuid", "A random UUID (version 4), such as 3b241101-e2bb-4255-8caf-4136c566a962."),
        new("$timestamp", "The current time in Unix seconds."),
        new("$isoTimestamp", "The current time in ISO 8601 (UTC), such as 2026-11-15T09:30:00.000Z."),
        new("$randomInt", "A random whole number from 0 to 1000; {{$randomInt min max}} picks one from min to max (both included)."),
    ];

    /// <summary>Whether <paramref name="name"/> (as written between the braces) is a built-in variable reference.</summary>
    public static bool IsDynamic(string name) => name.Length > 1 && name[0] == Prefix;

    /// <summary>
    /// Generates the value of a built-in reference such as <c>$uuid</c> or <c>$randomInt 1 6</c>;
    /// <c>null</c> when the name isn't a supported variable or its arguments are invalid.
    /// </summary>
    public static string? Generate(string reference, DateTimeOffset now, Random random)
    {
        var parts = reference.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        var args = parts[1..];
        switch (parts[0])
        {
            case "$uuid" when args.Length == 0:
                return NewUuid(random).ToString("D");
            case "$timestamp" when args.Length == 0:
                return now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            case "$isoTimestamp" when args.Length == 0:
                return now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
            case "$randomInt":
                return RandomInt(args, random);
            default:
                return null;
        }
    }

    /// <summary>
    /// <paramref name="variables"/> plus the built-in variables, generated on first use with
    /// <paramref name="now"/> and <paramref name="random"/> and then kept, so every use of the
    /// same reference in one send gets the same value. A variable the dictionary defines with a
    /// <c>$</c> name wins over the built-in one.
    /// </summary>
    public static IReadOnlyDictionary<string, string> With(
        IReadOnlyDictionary<string, string> variables,
        DateTimeOffset now,
        Random random) =>
        variables is Generated generated ? generated : new Generated(variables, now, random);

    private static string? RandomInt(string[] args, Random random)
    {
        long min = 0;
        long max = DefaultRandomMax;
        if (args.Length == 2)
        {
            if (!long.TryParse(args[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out min)
                || !long.TryParse(args[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out max)
                || min > max || max == long.MaxValue)
            {
                return null;
            }
        }
        else if (args.Length != 0)
        {
            return null;
        }

        return random.NextInt64(min, max + 1).ToString(CultureInfo.InvariantCulture);
    }

    private static Guid NewUuid(Random random)
    {
        Span<byte> bytes = stackalloc byte[16];
        random.NextBytes(bytes);
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x40); // version 4
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // RFC 4122 variant
        return new Guid(bytes, bigEndian: true);
    }

    /// <summary>A read-only view that adds the generated values to the variables it wraps.</summary>
    private sealed class Generated(IReadOnlyDictionary<string, string> variables, DateTimeOffset now, Random random)
        : IReadOnlyDictionary<string, string>
    {
        private readonly Dictionary<string, string> generated = new(StringComparer.Ordinal);
        private readonly Lock gate = new();

        public string this[string key] => TryGetValue(key, out var value) ? value : throw new KeyNotFoundException(key);

        public IEnumerable<string> Keys => variables.Keys;

        public IEnumerable<string> Values => variables.Values;

        public int Count => variables.Count;

        public bool ContainsKey(string key) => TryGetValue(key, out _);

        public bool TryGetValue(string key, [MaybeNullWhen(false)] out string value)
        {
            if (variables.TryGetValue(key, out value))
            {
                return true;
            }

            if (!IsDynamic(key))
            {
                return false;
            }

            var normalized = string.Join(' ', key.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries));
            lock (gate)
            {
                if (generated.TryGetValue(normalized, out value))
                {
                    return true;
                }

                value = Generate(normalized, now, random);
                if (value is null)
                {
                    return false;
                }

                generated[normalized] = value;
                return true;
            }
        }

        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => variables.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
