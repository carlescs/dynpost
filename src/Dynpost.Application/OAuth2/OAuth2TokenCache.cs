using Dynpost.Application.Common;

namespace Dynpost.Application.OAuth2;

/// <summary>
/// Changes to the stored OAuth 2.0 tokens, shared by the use cases that touch them. Every change
/// loads the store, changes it and saves it under one lock, so two requests refreshing tokens at
/// once never save over each other's work.
/// </summary>
internal static class OAuth2TokenCache
{
    private static readonly SemaphoreSlim Lock = new(1, 1);

    public static async Task<Result<OAuth2Token?>> GetAsync(
        IOAuth2TokenStore store,
        string root,
        string environmentId,
        string key,
        CancellationToken cancellationToken)
    {
        var all = await store.LoadAsync(root, cancellationToken);
        return all.IsSuccess
            ? Result<OAuth2Token?>.Success(all.Value.GetValueOrDefault(environmentId)?.GetValueOrDefault(key))
            : all.Error!;
    }

    /// <summary>Stores a token, or forgets it when <paramref name="token"/> is <c>null</c>.</summary>
    public static Task<Result<Unit>> SetAsync(
        IOAuth2TokenStore store,
        string root,
        string environmentId,
        string key,
        OAuth2Token? token,
        CancellationToken cancellationToken) =>
        ChangeAsync(store, root, all =>
        {
            var tokens = new Dictionary<string, OAuth2Token>(
                all.GetValueOrDefault(environmentId) ?? new Dictionary<string, OAuth2Token>(),
                StringComparer.Ordinal);
            if (token is null)
            {
                if (!tokens.Remove(key))
                {
                    return null;
                }
            }
            else
            {
                tokens[key] = token;
            }

            var updated = Copy(all);
            updated[environmentId] = tokens;
            return updated;
        }, cancellationToken);

    /// <summary>Moves an environment's tokens when it is renamed. Failures only leave local leftovers.</summary>
    public static Task MoveAsync(IOAuth2TokenStore store, string root, string from, string to, CancellationToken cancellationToken) =>
        ChangeAsync(store, root, all =>
        {
            if (!all.TryGetValue(from, out var tokens))
            {
                return null;
            }

            var updated = Copy(all);
            updated.Remove(from);
            updated[to] = tokens;
            return updated;
        }, cancellationToken);

    /// <summary>Forgets an environment's tokens, e.g. when it is deleted. Failures only leave local leftovers.</summary>
    public static Task RemoveAsync(IOAuth2TokenStore store, string root, string environmentId, CancellationToken cancellationToken) =>
        ChangeAsync(store, root, all =>
        {
            if (!all.ContainsKey(environmentId))
            {
                return null;
            }

            var updated = Copy(all);
            updated.Remove(environmentId);
            return updated;
        }, cancellationToken);

    /// <param name="change">Returns the new tokens, or <c>null</c> when nothing changes.</param>
    private static async Task<Result<Unit>> ChangeAsync(
        IOAuth2TokenStore store,
        string root,
        Func<IReadOnlyDictionary<string, IReadOnlyDictionary<string, OAuth2Token>>, Dictionary<string, IReadOnlyDictionary<string, OAuth2Token>>?> change,
        CancellationToken cancellationToken)
    {
        await Lock.WaitAsync(cancellationToken);
        try
        {
            var all = await store.LoadAsync(root, cancellationToken);
            if (!all.IsSuccess)
            {
                return all.Error!;
            }

            var updated = change(all.Value);
            return updated is null ? Unit.Value : await store.SaveAsync(root, updated, cancellationToken);
        }
        finally
        {
            Lock.Release();
        }
    }

    private static Dictionary<string, IReadOnlyDictionary<string, OAuth2Token>> Copy(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, OAuth2Token>> all) =>
        all.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
}
