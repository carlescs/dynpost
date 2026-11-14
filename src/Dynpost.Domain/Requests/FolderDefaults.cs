using Dynpost.Domain.Environments;
using Dynpost.Domain.Workspaces;

namespace Dynpost.Domain.Requests;

/// <summary>
/// The headers, auth and variables a collection folder sets for every request and sub-folder in
/// it: the content of its <c>_folder.yaml</c> file. Values may contain <c>{{variable}}</c> references.
/// </summary>
public sealed record FolderDefaults
{
    public FolderDefaults(
        IReadOnlyList<KeyValueEntry>? headers = null,
        RequestAuth? auth = null,
        IReadOnlyList<KeyValueEntry>? variables = null)
    {
        Headers = headers ?? [];
        Auth = auth ?? RequestAuth.Inherit;
        Variables = variables ?? [];
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

    /// <summary>
    /// Variables for every request below the folder, unless a closer folder sets the same one. They
    /// are defaults: the selected environment's variables (and the values captured for it) win.
    /// Disabled variables are not used.
    /// </summary>
    public IReadOnlyList<KeyValueEntry> Variables { get; init; }

    /// <summary>Whether the folder sets nothing, so it needs no file.</summary>
    public bool IsEmpty => Headers.Count == 0 && Auth is InheritAuth && Variables.Count == 0;

    /// <summary>Checks the rules folder defaults must satisfy before they can be saved; empty when valid.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        HttpRequestDefinition.CheckHeaders(Headers, errors);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var variable in Variables)
        {
            if (!VariableTemplate.IsValidName(variable.Key))
            {
                errors.Add(variable.Key.Length == 0
                    ? "Every variable needs a name."
                    : $"'{variable.Key}' is not a valid variable name: it can't contain spaces or braces.");
            }
            else if (!names.Add(variable.Key))
            {
                errors.Add($"The variable '{variable.Key}' is defined more than once.");
            }
        }

        return errors;
    }
}

/// <summary>A variable a request gets from a folder above it.</summary>
/// <param name="Value">Its value as written, possibly with <c>{{variable}}</c> references.</param>
/// <param name="Folder">The folder whose <c>_folder.yaml</c> sets it.</param>
public sealed record InheritedVariable(string Name, string Value, WorkspacePath Folder);

/// <summary>A header a request gets from a folder above it.</summary>
/// <param name="Folder">The folder whose <c>_folder.yaml</c> sets it.</param>
public sealed record InheritedHeader(string Key, string Value, WorkspacePath Folder);

/// <summary>
/// What a request (or a sub-folder) inherits from the folders above it. Folders apply from the
/// outermost down, and a closer level always wins: a folder's header replaces a header of the same
/// name (ignoring case) set further up, and the request's own enabled headers replace both; the
/// auth is that of the closest level that doesn't inherit it. Disabled headers are not sent, so
/// they neither apply nor replace anything. Variables work like headers (names are case-sensitive);
/// see <see cref="ResolveVariables"/> for how they combine with the environment's.
/// </summary>
public sealed record InheritedDefaults(IReadOnlyList<InheritedHeader> Headers, RequestAuth Auth, WorkspacePath? AuthFolder)
{
    /// <summary>Nothing inherited: no folder above sets headers, auth or variables.</summary>
    public static InheritedDefaults None { get; } = new([], RequestAuth.Inherit, null);

    /// <summary>The variables set by the folders, the closest one's when several set the same name.</summary>
    public IReadOnlyList<InheritedVariable> Variables { get; init; } = [];

    /// <summary>Combines the defaults of a chain of folders, the outermost first.</summary>
    public static InheritedDefaults From(IEnumerable<(WorkspacePath Folder, FolderDefaults Defaults)> chain)
    {
        var headers = new List<InheritedHeader>();
        var variables = new List<InheritedVariable>();
        RequestAuth auth = RequestAuth.Inherit;
        WorkspacePath? authFolder = null;
        foreach (var (folder, defaults) in chain)
        {
            foreach (var header in defaults.Headers.Where(header => header.Enabled))
            {
                headers.RemoveAll(existing => SameName(existing.Key, header.Key));
                headers.Add(new InheritedHeader(header.Key, header.Value, folder));
            }

            foreach (var variable in defaults.Variables.Where(variable => variable.Enabled))
            {
                variables.RemoveAll(existing => existing.Name == variable.Key);
                variables.Add(new InheritedVariable(variable.Key, variable.Value, folder));
            }

            if (defaults.Auth is not InheritAuth)
            {
                auth = defaults.Auth;
                authFolder = folder;
            }
        }

        return new InheritedDefaults(headers, auth, authFolder) { Variables = variables };
    }

    /// <summary>
    /// The variables a request is sent with: the folders' variables as defaults, with
    /// <paramref name="environment"/> (the selected environment's resolved values) winning over
    /// them. A folder variable's value may refer to environment variables and to other folder
    /// variables; environment values are final and are not expanded again. Folder variables that
    /// refer to each other in a loop are left out and listed in <see cref="ResolvedVariables.Cycle"/>.
    /// </summary>
    public ResolvedVariables ResolveVariables(IReadOnlyDictionary<string, string> environment)
    {
        var own = Variables.Where(variable => !environment.ContainsKey(variable.Name)).ToList();
        if (own.Count == 0)
        {
            return new ResolvedVariables(environment, []);
        }

        // Environment values take part as placeholders no folder value can contain, so they are
        // never expanded as templates themselves.
        var placeholders = environment.Keys
            .Select((name, index) => (Name: name, Placeholder: $"\0env:{index}\0"))
            .ToList();
        var resolved = VariableSet.Resolve(
            own.Select(variable => new EnvironmentVariable(variable.Name, variable.Value))
                .Concat(placeholders.Select(p => new EnvironmentVariable(p.Name, p.Placeholder))));

        var values = new Dictionary<string, string>(environment, StringComparer.Ordinal);
        foreach (var variable in own)
        {
            if (resolved.Values.TryGetValue(variable.Name, out var value))
            {
                values[variable.Name] = value.Contains('\0', StringComparison.Ordinal)
                    ? placeholders.Aggregate(value, (text, p) => text.Replace(p.Placeholder, environment[p.Name], StringComparison.Ordinal))
                    : value;
            }
        }

        return new ResolvedVariables(values, resolved.Cycle);
    }

    /// <summary>The folders that set the variables in <paramref name="cycle"/>, to say where to fix them.</summary>
    public IReadOnlyList<WorkspacePath> FoldersOf(IReadOnlyList<string> cycle) =>
        Variables.Where(variable => cycle.Contains(variable.Name)).Select(variable => variable.Folder).Distinct().ToList();

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
