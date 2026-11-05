using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Missive.Application.Common;
using Missive.Application.Git;
using Missive.Application.Settings;
using Missive.Application.Workspaces;
using Missive.Desktop.Bridge;
using Missive.Domain.Git;
using NSubstitute;

namespace Missive.Desktop.Tests.Bridge;

/// <summary>The git and last-workspace messages, through the real router and use cases.</summary>
public sealed class GitBridgeTests : IDisposable
{
    private const string Folder = "/work/my-api";

    private readonly IGitService _git = Substitute.For<IGitService>();
    private readonly IWorkspaceRepository _repository = Substitute.For<IWorkspaceRepository>();
    private readonly IAppSettingsStore _settings = Substitute.For<IAppSettingsStore>();
    private readonly CurrentWorkspace _current = new();
    private readonly ServiceProvider _services;
    private readonly BridgeRouter _router;

    public GitBridgeTests()
    {
        _services = new ServiceCollection()
            .AddSingleton(_git)
            .AddSingleton(_repository)
            .AddSingleton(_settings)
            .AddSingleton(_current)
            .AddTransient<OpenWorkspace>()
            .AddTransient<OpenLastWorkspace>()
            .AddTransient<GetGitStatus>()
            .AddTransient<CommitChanges>()
            .AddTransient<PullChanges>()
            .AddTransient<PushChanges>()
            .AddTransient<GetConflict>()
            .AddTransient<ResolveConflict>()
            .AddTransient<AbortMerge>()
            .BuildServiceProvider();
        _router = BridgeHandlers.Create(_services, Substitute.For<IFolderPicker>());
    }

    public void Dispose() => _services.Dispose();

    [Fact]
    public async Task GitStatus_WithoutAWorkspace_IsReported()
    {
        var response = await SendAsync("git.status");

        Assert.False(response.GetProperty("ok").GetBoolean());
        Assert.Equal(ErrorCodes.NoWorkspace, ErrorCode(response));
    }

    [Fact]
    public async Task GitStatus_ReturnsTheStatusWithStringStatuses()
    {
        _current.Open(Folder);
        _git.GetStatusAsync(Folder, Arg.Any<CancellationToken>()).Returns(Result<GitStatus>.Success(
            new GitStatus(
                "main",
                null,
                1,
                0,
                false,
                [
                    new GitChange("users/get-user.yaml", GitFileStatus.Modified),
                    new GitChange("new.yaml", GitFileStatus.Renamed, "old.yaml"),
                ])));

        var response = await SendAsync("git.status");

        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        var payload = response.GetProperty("payload");
        Assert.Equal("main", payload.GetProperty("branch").GetString());
        Assert.Equal(1, payload.GetProperty("ahead").GetInt32());
        Assert.False(payload.GetProperty("merging").GetBoolean());
        var changes = payload.GetProperty("changes");
        Assert.Equal("users/get-user.yaml", changes[0].GetProperty("path").GetString());
        Assert.Equal("modified", changes[0].GetProperty("status").GetString());
        Assert.Equal("renamed", changes[1].GetProperty("status").GetString());
        Assert.Equal("old.yaml", changes[1].GetProperty("originalPath").GetString());
    }

    [Fact]
    public async Task GitCommit_PassesTheMessageAndPaths()
    {
        _current.Open(Folder);
        _git.GetStatusAsync(Folder, Arg.Any<CancellationToken>()).Returns(
            Result<GitStatus>.Success(new GitStatus("main", "origin/main", 0, 0, false, [new GitChange("a.yaml", GitFileStatus.Modified)])),
            Result<GitStatus>.Success(new GitStatus("main", "origin/main", 1, 0, false, [])));
        _git.CommitAsync(default!, default!, default, default).ReturnsForAnyArgs(Result<string>.Success("abc123"));

        var response = await SendAsync("git.commit", """{ "message": "Update A", "paths": ["a.yaml"] }""");

        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        Assert.Equal("abc123", response.GetProperty("payload").GetProperty("commitId").GetString());
        _ = _git.Received(1).CommitAsync(
            Folder,
            Arg.Is<CommitMessage>(m => m.Value == "Update A"),
            Arg.Is<IReadOnlyList<string>?>(p => p != null && p.SequenceEqual(new[] { "a.yaml" })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GitCommit_WithoutAMessage_IsReported()
    {
        _current.Open(Folder);

        var response = await SendAsync("git.commit", """{ "message": "  " }""");

        Assert.Equal(GitErrors.InvalidCommitMessageCode, ErrorCode(response));
    }

    [Fact]
    public async Task GitPull_ReturnsTheOutcome()
    {
        _current.Open(Folder);
        _git.GetStatusAsync(Folder, Arg.Any<CancellationToken>())
            .Returns(Result<GitStatus>.Success(new GitStatus("main", "origin/main", 0, 0, false, [])));
        _git.PullAsync(Folder, Arg.Any<CancellationToken>()).Returns(Result<PullOutcome>.Success(PullOutcome.UpToDate));

        var response = await SendAsync("git.pull");

        Assert.Equal("upToDate", response.GetProperty("payload").GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task GitPush_Rejected_IsReported()
    {
        _current.Open(Folder);
        _git.GetStatusAsync(Folder, Arg.Any<CancellationToken>())
            .Returns(Result<GitStatus>.Success(new GitStatus("main", "origin/main", 1, 0, false, [])));
        _git.PushAsync(default!, default, default)
            .ReturnsForAnyArgs(Result<Unit>.Failure(GitErrors.PushRejected("! refs/heads/main [rejected]")));

        var response = await SendAsync("git.push");

        Assert.Equal(GitErrors.PushRejectedCode, ErrorCode(response));
    }

    [Fact]
    public async Task OpenLast_WithNothingRemembered_ReturnsNull()
    {
        _settings.LoadAsync(Arg.Any<CancellationToken>()).Returns(AppSettings.Default);

        var response = await SendAsync("workspace.openLast");

        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        Assert.Equal(JsonValueKind.Null, response.GetProperty("payload").ValueKind);
    }

    [Fact]
    public async Task GitConflict_ReturnsBothSides()
    {
        _current.Open(Folder);
        _git.GetStatusAsync(Folder, Arg.Any<CancellationToken>()).Returns(Result<GitStatus>.Success(
            new GitStatus("main", "origin/main", 1, 1, true, [new GitChange("a.yaml", GitFileStatus.Conflicted)], "Merge branch 'main'")));
        _git.GetConflictAsync(Folder, "a.yaml", Arg.Any<CancellationToken>()).Returns(Result<GitConflict>.Success(
            new GitConflict("a.yaml", "name: A\n", "name: Bob\n", null, "name: Bob\n")));

        var response = await SendAsync("git.conflict", """{ "path": "a.yaml" }""");

        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        var payload = response.GetProperty("payload");
        Assert.Equal("name: Bob\n", payload.GetProperty("ours").GetString());
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("theirs").ValueKind);
        Assert.False(payload.GetProperty("hasConflictMarkers").GetBoolean());
    }

    [Theory]
    [InlineData("ours", ConflictResolution.Ours)]
    [InlineData("theirs", ConflictResolution.Theirs)]
    public async Task GitResolve_PassesTheUsersChoice(string name, ConflictResolution resolution)
    {
        _current.Open(Folder);
        _git.GetStatusAsync(Folder, Arg.Any<CancellationToken>()).Returns(
            Result<GitStatus>.Success(new GitStatus("main", "origin/main", 1, 1, true, [new GitChange("a.yaml", GitFileStatus.Conflicted)])),
            Result<GitStatus>.Success(new GitStatus("main", "origin/main", 1, 1, true, [new GitChange("a.yaml", GitFileStatus.Modified)], "Merge")));
        _git.ResolveConflictAsync(default!, default!, default, default).ReturnsForAnyArgs(Result<Unit>.Success(Unit.Value));

        var response = await SendAsync("git.resolve", $$"""{ "path": "a.yaml", "resolution": "{{name}}" }""");

        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        Assert.Equal("Merge", response.GetProperty("payload").GetProperty("mergeMessage").GetString());
        _ = _git.Received(1).ResolveConflictAsync(Folder, "a.yaml", resolution, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GitResolve_UnknownResolution_IsRejected()
    {
        _current.Open(Folder);

        var response = await SendAsync("git.resolve", """{ "path": "a.yaml", "resolution": "both" }""");

        Assert.Equal("bad_payload", ErrorCode(response));
        _ = _git.DidNotReceiveWithAnyArgs().ResolveConflictAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task GitAbortMerge_WithoutAMerge_IsReported()
    {
        _current.Open(Folder);
        _git.GetStatusAsync(Folder, Arg.Any<CancellationToken>())
            .Returns(Result<GitStatus>.Success(new GitStatus("main", "origin/main", 0, 0, false, [])));

        var response = await SendAsync("git.abortMerge");

        Assert.Equal(GitErrors.NotMergingCode, ErrorCode(response));
    }

    private async Task<JsonElement> SendAsync(string type, string? payload = null)
    {
        var message = payload is null
            ? $$"""{ "id": "1", "type": "{{type}}" }"""
            : $$"""{ "id": "1", "type": "{{type}}", "payload": {{payload}} }""";
        var json = await _router.HandleAsync(message);
        Assert.NotNull(json);
        return JsonDocument.Parse(json).RootElement;
    }

    private static string? ErrorCode(JsonElement response) =>
        response.GetProperty("error").GetProperty("code").GetString();
}
