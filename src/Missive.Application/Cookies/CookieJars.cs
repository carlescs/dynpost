using Missive.Application.Common;
using Missive.Domain.Http;

namespace Missive.Application.Cookies;

/// <summary>Changes to the stored cookie jars, shared by the use cases that touch them.</summary>
internal static class CookieJars
{
    /// <summary>The jar of one environment, without expired cookies, sorted for display.</summary>
    public static async Task<Result<IReadOnlyList<StoredCookie>>> LoadAsync(
        ICookieJarStore store,
        string root,
        string environmentId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var jars = await store.LoadAsync(root, cancellationToken);
        if (!jars.IsSuccess)
        {
            return jars.Error!;
        }

        return Result<IReadOnlyList<StoredCookie>>.Success(Sorted(jars.Value.GetValueOrDefault(environmentId) ?? [], now));
    }

    /// <summary>Replaces one environment's jar (an empty list removes it) and saves every jar.</summary>
    public static async Task<Result<Unit>> ReplaceAsync(
        ICookieJarStore store,
        string root,
        string environmentId,
        IReadOnlyList<StoredCookie> cookies,
        CancellationToken cancellationToken)
    {
        var jars = await store.LoadAsync(root, cancellationToken);
        if (!jars.IsSuccess)
        {
            return jars.Error!;
        }

        var current = jars.Value.GetValueOrDefault(environmentId) ?? [];
        if (current.SequenceEqual(cookies))
        {
            return Unit.Value;
        }

        var updated = jars.Value
            .Where(entry => entry.Key != environmentId)
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
        if (cookies.Count > 0)
        {
            updated[environmentId] = cookies;
        }

        return await store.SaveAsync(root, updated, cancellationToken);
    }

    /// <summary>Moves a jar to a renamed environment; does nothing when there is none.</summary>
    public static async Task MoveAsync(ICookieJarStore store, string root, string from, string to, CancellationToken cancellationToken)
    {
        var jars = await store.LoadAsync(root, cancellationToken);
        if (!jars.IsSuccess || !jars.Value.TryGetValue(from, out var cookies))
        {
            return;
        }

        var updated = jars.Value
            .Where(entry => entry.Key != from && entry.Key != to)
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
        updated[to] = cookies;
        await store.SaveAsync(root, updated, cancellationToken);
    }

    /// <summary>Removes an environment's jar, if it has one. Failures are ignored: leftovers are only local.</summary>
    public static async Task RemoveAsync(ICookieJarStore store, string root, string environmentId, CancellationToken cancellationToken)
    {
        var jars = await store.LoadAsync(root, cancellationToken);
        if (jars.IsSuccess && jars.Value.ContainsKey(environmentId))
        {
            await ReplaceAsync(store, root, environmentId, [], cancellationToken);
        }
    }

    public static IReadOnlyList<StoredCookie> Sorted(IEnumerable<StoredCookie> cookies, DateTimeOffset now) =>
        cookies
            .Where(cookie => !cookie.IsExpired(now))
            .OrderBy(cookie => cookie.Domain.TrimStart('.'), StringComparer.OrdinalIgnoreCase)
            .ThenBy(cookie => cookie.Path, StringComparer.Ordinal)
            .ThenBy(cookie => cookie.Name, StringComparer.Ordinal)
            .ToList();
}
