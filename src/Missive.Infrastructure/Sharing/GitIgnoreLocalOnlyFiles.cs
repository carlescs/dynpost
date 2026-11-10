using System.Text;
using Missive.Application.Common;
using Missive.Application.Sharing;
using Missive.Domain.Workspaces;
using Missive.Infrastructure.Git;

namespace Missive.Infrastructure.Sharing;

/// <summary>
/// <see cref="ILocalOnlyFiles"/> kept in the workspace's <c>.gitignore</c>, one anchored line per
/// file, so git itself (and any other git tool) leaves local-only files out. The file is only
/// rewritten when an entry actually changes.
/// </summary>
public sealed class GitIgnoreLocalOnlyFiles : ILocalOnlyFiles
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public Result<IReadOnlySet<WorkspacePath>> List(string root)
    {
        var content = Read(root);
        return content.IsSuccess ? Result<IReadOnlySet<WorkspacePath>>.Success(GitIgnoreEntries.List(content.Value)) : content.Error!;
    }

    public Result<bool> Set(string root, WorkspacePath path, bool localOnly) =>
        Update(root, content => localOnly
            ? GitIgnoreEntries.Add(GitIgnoreFile.AddLocalFolder(content), path)
            : GitIgnoreEntries.Remove(content, path));

    public Result<Unit> Move(string root, WorkspacePath from, WorkspacePath to) =>
        Done(Update(root, content => GitIgnoreEntries.Move(content, from, to)));

    public Result<Unit> Copy(string root, WorkspacePath from, WorkspacePath to) =>
        Done(Update(root, content => GitIgnoreEntries.Copy(content, from, to)));

    public Result<Unit> Remove(string root, WorkspacePath path) =>
        Done(Update(root, content => GitIgnoreEntries.Remove(content, path)));

    private static Result<bool> Update(string root, Func<string, string> change)
    {
        var content = Read(root);
        if (!content.IsSuccess)
        {
            return content.Error!;
        }

        var updated = change(content.Value);
        if (updated == content.Value)
        {
            return false;
        }

        try
        {
            File.WriteAllText(FilePath(root), updated, Utf8NoBom);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Error(ErrorCodes.Io, $"Could not update {GitIgnoreFile.FileName}: {ex.Message}");
        }
    }

    private static Result<string> Read(string root)
    {
        var path = FilePath(root);
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Error(ErrorCodes.Io, $"Could not read {GitIgnoreFile.FileName}: {ex.Message}");
        }
    }

    private static string FilePath(string root) => Path.Combine(root, GitIgnoreFile.FileName);

    private static Result<Unit> Done(Result<bool> result) =>
        result.IsSuccess ? Unit.Value : result.Error!;
}
