using Dynypost.Application.Common;
using Dynypost.Application.Environments;
using Dynypost.Application.Workspaces;

namespace Dynypost.Application.Cookies;

/// <summary>Lists the cookies kept for an environment.</summary>
public sealed class ListCookies(ICookieJarStore store, CurrentWorkspace current, TimeProvider time)
{
    public async Task<Result<IReadOnlyList<CookieDto>>> ExecuteAsync(string environmentId, CancellationToken cancellationToken = default)
    {
        var root = current.RequireRoot();
        if (!root.IsSuccess)
        {
            return root.Error!;
        }

        if (!EnvironmentMapper.IsValidId(environmentId))
        {
            return EnvironmentMapper.InvalidId(environmentId);
        }

        var cookies = await CookieJars.LoadAsync(store, root.Value, environmentId, time.GetUtcNow(), cancellationToken);
        return cookies.Map<IReadOnlyList<CookieDto>>(list => list.Select(CookieDto.From).ToList());
    }
}

/// <summary>Deletes one cookie from an environment's jar and returns what is left.</summary>
public sealed class DeleteCookie(ICookieJarStore store, CurrentWorkspace current, TimeProvider time)
{
    public async Task<Result<IReadOnlyList<CookieDto>>> ExecuteAsync(
        string environmentId,
        string name,
        string domain,
        string path,
        CancellationToken cancellationToken = default)
    {
        var root = current.RequireRoot();
        if (!root.IsSuccess)
        {
            return root.Error!;
        }

        if (!EnvironmentMapper.IsValidId(environmentId))
        {
            return EnvironmentMapper.InvalidId(environmentId);
        }

        var now = time.GetUtcNow();
        var remaining = await CookieJars.UpdateAsync(
            store,
            root.Value,
            environmentId,
            cookies => CookieJars.Sorted(cookies.Where(cookie => !cookie.SameIdentity(name, domain, path)), now),
            cancellationToken);
        return remaining.Map<IReadOnlyList<CookieDto>>(list => list.Select(CookieDto.From).ToList());
    }
}

/// <summary>Deletes every cookie kept for an environment.</summary>
public sealed class ClearCookies(ICookieJarStore store, CurrentWorkspace current)
{
    public async Task<Result<Unit>> ExecuteAsync(string environmentId, CancellationToken cancellationToken = default)
    {
        var root = current.RequireRoot();
        if (!root.IsSuccess)
        {
            return root.Error!;
        }

        return EnvironmentMapper.IsValidId(environmentId)
            ? await CookieJars.ReplaceAsync(store, root.Value, environmentId, [], cancellationToken)
            : EnvironmentMapper.InvalidId(environmentId);
    }
}
