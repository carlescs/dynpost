using Dynpost.Domain.Workspaces;

namespace Dynpost.Domain.Requests;

/// <summary>
/// The headers and auth a collection folder sets for every request and sub-folder in it: the
/// content of its <c>_folder.yaml</c> file. Values may contain <c>{{variable}}</c> references.
/// </summary>
public sealed record FolderDefaults
{
    public FolderDefaults(IReadOnlyList<KeyValueEntry>? headers = null, RequestAuth? auth = null)
    {
        Headers = headers ?? [];
        Auth = auth ?? RequestAuth.Inherit;
    }

    /// <summary>A folder that sets nothing, as when it has no <c>_folder.yaml</c>.</summary>
    public static FolderDefaults Empty { get; } = new();

    /// <summary>Headers sent with every request below the folder, unless a closer level sets the same one.</summary>
    public IReadOnlyList<KeyValueEntry> Headers { get; init; }

    /// <summary>
    /// The auth of every request below the folder that inherits it; <see cref="InheritAuth"/>
    /// passes on the auth of the folders above.
    /// </summary>
    public RequestAuth Auth { get; init; }

    /// <summary>Whether the folder sets nothing, so it needs no file.</summary>
    public bool IsEmpty => Headers.Count == 0 && Auth is InheritAuth;

    /// <summary>Checks the rules folder defaults must satisfy before they can be saved; empty when valid.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        HttpRequestDefinition.CheckHeaders(Headers, errors);
        return errors;
    }
}

/// <summary>A header a request gets from a folder above it.</summary>
/// <param name="Folder">The folder whose <c>_folder.yaml</c> sets it.</param>
public sealed record InheritedHeader(string Key, string Value, WorkspacePath Folder);

/// <summary>
/// What a request (or a sub-folder) inherits from the folders above it. Folders apply from the
/// outermost down, and a closer level always wins: a folder's header replaces a header of the same
/// name (ignoring case) set further up, and the request's own enabled headers replace both; the
/// auth is that of the closest level that doesn't inherit it. Disabled headers are not sent, so
/// they neither apply nor replace anything.
/// </summary>
public sealed record InheritedDefaults(IReadOnlyList<InheritedHeader> Headers, RequestAuth Auth, WorkspacePath? AuthFolder)
{
    /// <summary>Nothing inherited: no folder above sets headers or auth.</summary>
    public static InheritedDefaults None { get; } = new([], RequestAuth.Inherit, null);

    /// <summary>Combines the defaults of a chain of folders, the outermost first.</summary>
    public static InheritedDefaults From(IEnumerable<(WorkspacePath Folder, FolderDefaults Defaults)> chain)
    {
        var headers = new List<InheritedHeader>();
        RequestAuth auth = RequestAuth.Inherit;
        WorkspacePath? authFolder = null;
        foreach (var (folder, defaults) in chain)
        {
            foreach (var header in defaults.Headers.Where(header => header.Enabled))
            {
                headers.RemoveAll(existing => SameName(existing.Key, header.Key));
                headers.Add(new InheritedHeader(header.Key, header.Value, folder));
            }

            if (defaults.Auth is not InheritAuth)
            {
                auth = defaults.Auth;
                authFolder = folder;
            }
        }

        return new InheritedDefaults(headers, auth, authFolder);
    }

    /// <summary>
    /// The request as it is sent: the inherited headers it doesn't set itself come first, then its
    /// own; an inherited auth replaces <see cref="InheritAuth"/>, which becomes no auth when no
    /// folder sets one.
    /// </summary>
    public HttpRequestDefinition Apply(HttpRequestDefinition request)
    {
        var own = request.Headers.Where(header => header.Enabled).Select(header => header.Key).ToList();
        var inherited = Headers
            .Where(header => !own.Any(name => SameName(name, header.Key)))
            .Select(header => new KeyValueEntry(header.Key, header.Value));

        return request with
        {
            Headers = [.. inherited, .. request.Headers],
            Auth = request.Auth is InheritAuth ? EffectiveAuth : request.Auth,
        };
    }

    /// <summary>The inherited auth, with <see cref="InheritAuth"/> (no folder sets one) as no auth.</summary>
    public RequestAuth EffectiveAuth => Auth is InheritAuth ? RequestAuth.None : Auth;

    private static bool SameName(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
