using Missive.Application.Common;
using Missive.Application.Requests;
using Missive.Application.Workspaces;
using Missive.Domain.Requests;
using Missive.Domain.Workspaces;

namespace Missive.Application.Folders;

/// <summary>Reads and writes the <c>_folder.yaml</c> files holding folder defaults.</summary>
public interface IFolderDefaultsStore
{
    /// <summary>
    /// The defaults set by <paramref name="folder"/>: <see cref="FolderDefaults.Empty"/> when it has
    /// no <c>_folder.yaml</c>, or why the file can't be used (<see cref="ErrorCodes.InvalidFile"/>).
    /// </summary>
    Task<Result<FolderDefaults>> ReadAsync(string root, WorkspacePath folder, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the defaults of <paramref name="folder"/>, or deletes its file when they are empty.
    /// Leaves the file untouched when its content would not change; the result says whether it changed.
    /// </summary>
    Task<Result<bool>> WriteAsync(string root, WorkspacePath folder, FolderDefaults defaults, CancellationToken cancellationToken = default);
}

/// <summary>The headers and auth a folder sets for everything in it.</summary>
/// <param name="Auth">Its auth; type <c>inherit</c> passes on the auth of the folders above.</param>
public sealed record FolderSettingsDto(IReadOnlyList<KeyValueDto> Headers, AuthDto Auth);

/// <summary>A header inherited from a folder.</summary>
/// <param name="Folder">The folder that sets it.</param>
public sealed record InheritedHeaderDto(string Key, string Value, string Folder);

/// <summary>What the requests and folders directly inside a folder inherit from it and the folders above.</summary>
/// <param name="Auth">The inherited auth; type <c>none</c> when no folder sets one.</param>
/// <param name="AuthFolder">The folder the auth comes from; <c>null</c> when no folder sets one.</param>
public sealed record InheritedSettingsDto(IReadOnlyList<InheritedHeaderDto> Headers, AuthDto Auth, string? AuthFolder);

/// <summary>
/// Works out what requests inherit from the <c>_folder.yaml</c> files of the folders they are in:
/// those of <c>requests/</c> and of each folder down to theirs, the closest winning.
/// </summary>
public sealed class FolderInheritance(IFolderDefaultsStore store)
{
    /// <summary>
    /// What everything directly inside <paramref name="folder"/> inherits. A folder outside
    /// <c>requests/</c> inherits nothing.
    /// </summary>
    public async Task<Result<InheritedDefaults>> ForFolderAsync(string root, WorkspacePath folder, CancellationToken cancellationToken = default)
    {
        if (!WorkspaceLayout.IsAreaFolder(WorkspaceLayout.RequestsRoot, folder))
        {
            return InheritedDefaults.None;
        }

        var folders = new List<WorkspacePath> { WorkspaceLayout.RequestsRoot };
        foreach (var segment in folder.Segments.Skip(WorkspaceLayout.RequestsRoot.Segments.Count))
        {
            folders.Add(folders[^1].Combine(segment));
        }

        var chain = new List<(WorkspacePath, FolderDefaults)>();
        foreach (var current in folders)
        {
            var defaults = await store.ReadAsync(root, current, cancellationToken);
            if (!defaults.IsSuccess)
            {
                return defaults.Error!;
            }

            chain.Add((current, defaults.Value));
        }

        return InheritedDefaults.From(chain);
    }

    /// <summary>The request as it is sent from <paramref name="folder"/>, with what it inherits applied.</summary>
    public async Task<Result<HttpRequestDefinition>> ApplyAsync(
        string root,
        WorkspacePath folder,
        HttpRequestDefinition request,
        CancellationToken cancellationToken = default)
    {
        var inherited = await ForFolderAsync(root, folder, cancellationToken);
        return inherited.Map(defaults => defaults.Apply(request));
    }
}

/// <summary>Reads the headers and auth a folder of the open workspace sets (its <c>_folder.yaml</c>).</summary>
public sealed class LoadFolderSettings(IWorkspaceRepository repository, IFolderDefaultsStore store, CurrentWorkspace current)
{
    public async Task<Result<FolderSettingsDto>> ExecuteAsync(string path, CancellationToken cancellationToken = default)
    {
        var root = current.RequireRoot();
        if (!root.IsSuccess)
        {
            return root.Error!;
        }

        if (!FolderPaths.TryParseFolder(repository, root.Value, path, allowRoot: true, out var folder, out var error))
        {
            return error;
        }

        var defaults = await store.ReadAsync(root.Value, folder, cancellationToken);
        return defaults.Map(FolderSettingsMapper.ToDto);
    }
}

/// <summary>
/// Saves the headers and auth a folder sets in its <c>_folder.yaml</c>, which is removed when it
/// sets nothing. Returns them as saved.
/// </summary>
public sealed class SaveFolderSettings(IWorkspaceRepository repository, IFolderDefaultsStore store, CurrentWorkspace current)
{
    public async Task<Result<FolderSettingsDto>> ExecuteAsync(string path, FolderSettingsDto settings, CancellationToken cancellationToken = default)
    {
        var root = current.RequireRoot();
        if (!root.IsSuccess)
        {
            return root.Error!;
        }

        if (!FolderPaths.TryParseFolder(repository, root.Value, path, allowRoot: true, out var folder, out var error))
        {
            return error;
        }

        var defaults = FolderSettingsMapper.ToDomain(settings);
        if (!defaults.IsSuccess)
        {
            return defaults.Error!;
        }

        var problems = defaults.Value.Validate();
        if (problems.Count > 0)
        {
            return new Error(ErrorCodes.InvalidRequest, string.Join(" ", problems));
        }

        var written = await store.WriteAsync(root.Value, folder, defaults.Value, cancellationToken);
        return written.IsSuccess ? FolderSettingsMapper.ToDto(defaults.Value) : written.Error!;
    }
}

/// <summary>
/// What the requests and folders directly inside a folder of the open workspace inherit from it
/// and the folders above: shown next to their own headers and auth.
/// </summary>
public sealed class GetInheritedSettings(IWorkspaceRepository repository, FolderInheritance inheritance, CurrentWorkspace current)
{
    public async Task<Result<InheritedSettingsDto>> ExecuteAsync(string folder, CancellationToken cancellationToken = default)
    {
        var root = current.RequireRoot();
        if (!root.IsSuccess)
        {
            return root.Error!;
        }

        if (!FolderPaths.TryParseFolder(repository, root.Value, folder, allowRoot: true, out var path, out var error))
        {
            return error;
        }

        var inherited = await inheritance.ForFolderAsync(root.Value, path, cancellationToken);
        return inherited.Map(defaults => new InheritedSettingsDto(
            defaults.Headers.Select(header => new InheritedHeaderDto(header.Key, header.Value, header.Folder.Value)).ToList(),
            RequestMapper.ToDto(defaults.EffectiveAuth),
            defaults.AuthFolder?.Value));
    }
}

internal static class FolderSettingsMapper
{
    public static FolderSettingsDto ToDto(FolderDefaults defaults) => new(
        defaults.Headers.Select(header => new KeyValueDto(header.Key, header.Value, header.Enabled)).ToList(),
        RequestMapper.ToDto(defaults.Auth));

    public static Result<FolderDefaults> ToDomain(FolderSettingsDto settings)
    {
        var auth = RequestMapper.ToDomain(settings.Auth);
        return auth.IsSuccess
            ? new FolderDefaults(RequestMapper.ToEntries(settings.Headers), auth.Value)
            : auth.Error!;
    }
}
