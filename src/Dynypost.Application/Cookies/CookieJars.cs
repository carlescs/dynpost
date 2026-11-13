using Dynypost.Application.Common;
using Dynypost.Domain.Http;

namespace Dynypost.Application.Cookies;

/// <summary>
/// Changes to the stored cookie jars, shared by the use cases that touch them. Every change loads
/// the jars, changes one environment's and saves them all, under one lock, so concurrent changes
/// (a send and a clear, say) never save over each other's work.
/// </summary>
internal static class CookieJars
{
    /// <summary>
    /// Guards every load-change-save of the jars. One lock for the whole app is enough: changes
    /// are small and rare, and a desktop app has one user.
    /// </summary>
    private static readonly SemaphoreSlim Lock = new(1, 1);

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

    /// <summary>
    /// Changes one environment's jar (an empty result removes it) and saves every jar; returns the
    /// changed jar. <paramref name="change"/> gets the jar as currently stored.
    /// </summary>
    public static async Task<Result<IReadOnlyList<StoredCookie>>> UpdateAsync(
        ICookieJarStore store,
        string root,
        string environmentId,
        Func<IReadOnlyList<StoredCookie>, IReadOnlyList<StoredCookie>> change,
        CancellationToken cancellationToken)
    {
        await Lock.WaitAsync(cancellationToken);
        try
        {
            var jars = await store.LoadAsync(root, cancellationToken);
            if (!jars.IsSuccess)
            {
                return jars.Error!;
            }

            var current = jars.Value.GetValueOrDefault(environmentId) ?? [];
            var cookies = change(current);
            if (current.SequenceEqual(cookies))
            {
                return Result<IReadOnlyList<StoredCookie>>.Success(cookies);
            }

            var updated = jars.Value
                .Where(entry => entry.Key != environmentId)
                .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
            if (cookies.Count > 0)
            {
                updated[environmentId] = cookies;
            }

            var saved = await store.SaveAsync(root, updated, cancellationToken);
            return saved.IsSuccess ? Result<IReadOnlyList<StoredCookie>>.Success(cookies) : saved.Error!;
        }
        finally
        {
            Lock.Release();
        }
    }

    /// <summary>Replaces one environment's jar (an empty list removes it) and saves every jar.</summary>
    public static async Task<Result<Unit>> ReplaceAsync(
        ICookieJarStore store,
        string root,
        string environmentId,
        IReadOnlyList<StoredCookie> cookies,
        CancellationToken cancellationToken)
    {
        var updated = await UpdateAsync(store, root, environmentId, _ => cookies, cancellationToken);
        return updated.IsSuccess ? Unit.Value : updated.Error!;
    }

    /// <summary>Moves a jar to a renamed environment; does nothing when there is none.</summary>
    public static async Task MoveAsync(ICookieJarStore store, string root, string from, string to, CancellationToken cancellationToken)
    {
        await Lock.WaitAsync(cancellationToken);
        try
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
        finally
        {
            Lock.Release();
        }
    }

    /// <summary>Removes an environment's jar, if it has one. Failures are ignored: leftovers are only local.</summary>
    public static Task RemoveAsync(ICookieJarStore store, string root, string environmentId, CancellationToken cancellationToken) =>
        UpdateAsync(store, root, environmentId, _ => [], cancellationToken);

    public static IReadOnlyList<StoredCookie> Sorted(IEnumerable<StoredCookie> cookies, DateTimeOffset now) =>
        cookies
            .Where(cookie => !cookie.IsExpired(now))
            .OrderBy(cookie => cookie.Domain.TrimStart('.'), StringComparer.OrdinalIgnoreCase)
            .ThenBy(cookie => cookie.Path, StringComparer.Ordinal)
            .ThenBy(cookie => cookie.Name, StringComparer.Ordinal)
            .ToList();
}
