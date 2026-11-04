using Missive.Application.Common;
using Missive.Domain.Git;

namespace Missive.Application.Tests.Git;

internal static class GitTestData
{
    public const string Workspace = "/work/my-api";

    public static GitStatus Status(
        string? branch = "main",
        string? upstream = "origin/main",
        bool merging = false,
        IReadOnlyList<GitChange>? changes = null) =>
        new(branch, upstream, 0, 0, merging, changes ?? []);

    public static GitStatus Clean() => Status();

    public static Result<T> Ok<T>(T value) => Result<T>.Success(value);
}
