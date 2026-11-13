using Dynpost.Application.Common;
using Dynpost.Application.Workspaces;
using Dynpost.Domain.Git;

namespace Dynpost.Application.Tests.Git;

internal static class GitTestData
{
    public const string WorkspacePath = "/work/my-api";

    public static GitStatus Status(
        string? branch = "main",
        string? upstream = "origin/main",
        bool merging = false,
        IReadOnlyList<GitChange>? changes = null) =>
        new(branch, upstream, 0, 0, merging, changes ?? []);

    public static GitStatus Clean() => Status();

    public static Result<T> Ok<T>(T value) => Result<T>.Success(value);

    /// <summary>A current workspace with <see cref="WorkspacePath"/> open.</summary>
    public static CurrentWorkspace OpenedWorkspace()
    {
        var workspace = new CurrentWorkspace();
        workspace.Open(WorkspacePath);
        return workspace;
    }
}
