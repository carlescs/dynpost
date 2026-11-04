using System.Text.Json;
using Missive.Application.Common;
using Missive.Application.Git;
using Missive.Desktop.Bridge;
using Missive.Desktop.Bridge.Git;
using Missive.Desktop.Bridge.Workspace;
using Missive.Domain.Git;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Missive.Desktop.Tests.Bridge;

public class BridgeDispatcherTests
{
    private const string Workspace = "/work/my-api";

    private readonly IGitService _git = Substitute.For<IGitService>();
    private readonly IFolderPicker _picker = Substitute.For<IFolderPicker>();
    private readonly BridgeDispatcher _dispatcher;

    public BridgeDispatcherTests()
    {
        _dispatcher = new BridgeDispatcher(
        [
            new PickWorkspaceFolderHandler(_picker),
            new GitStatusHandler(new GetGitStatus(_git)),
            new GitCommitHandler(new CommitChanges(_git)),
            new GitPullHandler(new PullChanges(_git)),
            new GitPushHandler(new PushChanges(_git)),
        ]);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "type": "git.status" }""")]
    [InlineData("""{ "id": "", "type": "git.status" }""")]
    public async Task MessagesWithoutAnId_AreIgnored(string message)
    {
        Assert.Null(await _dispatcher.DispatchAsync(message));
    }

    [Fact]
    public async Task UnknownType_IsReported()
    {
        var response = await DispatchAsync("""{ "id": "7", "type": "nope", "payload": {} }""");

        Assert.Equal("7", response.GetProperty("id").GetString());
        Assert.False(response.GetProperty("ok").GetBoolean());
        Assert.Equal(BridgeErrors.UnknownTypeCode, ErrorCode(response));
    }

    [Theory]
    [InlineData("""{ "id": "1", "type": "git.status" }""")]
    [InlineData("""{ "id": "1", "type": "git.status", "payload": {} }""")]
    [InlineData("""{ "id": "1", "type": "git.status", "payload": { "workspacePath": null } }""")]
    [InlineData("""{ "id": "1", "type": "git.status", "payload": { "workspacePath": 5 } }""")]
    public async Task InvalidPayload_IsReported(string message)
    {
        var response = await DispatchAsync(message);

        Assert.Equal(BridgeErrors.InvalidPayloadCode, ErrorCode(response));
    }

    [Fact]
    public async Task GitStatus_ReturnsTheStatusInCamelCase()
    {
        _git.GetStatusAsync(Workspace, Arg.Any<CancellationToken>()).Returns(Result<GitStatus>.Success(
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

        var response = await DispatchAsync(
            """{ "id": "1", "type": "git.status", "payload": { "workspacePath": "/work/my-api" } }""");

        Assert.True(response.GetProperty("ok").GetBoolean());
        var payload = response.GetProperty("payload");
        Assert.Equal("main", payload.GetProperty("branch").GetString());
        Assert.False(payload.TryGetProperty("upstream", out _));
        Assert.Equal(1, payload.GetProperty("ahead").GetInt32());
        Assert.False(payload.GetProperty("merging").GetBoolean());
        var changes = payload.GetProperty("changes");
        Assert.Equal("users/get-user.yaml", changes[0].GetProperty("path").GetString());
        Assert.Equal("modified", changes[0].GetProperty("status").GetString());
        Assert.Equal("renamed", changes[1].GetProperty("status").GetString());
        Assert.Equal("old.yaml", changes[1].GetProperty("originalPath").GetString());
    }

    [Fact]
    public async Task UseCaseErrors_AreReturnedWithTheirCode()
    {
        _git.GetStatusAsync(Workspace, Arg.Any<CancellationToken>())
            .Returns(Result<GitStatus>.Failure(GitErrors.NotARepository(Workspace)));

        var response = await DispatchAsync(
            """{ "id": "2", "type": "git.status", "payload": { "workspacePath": "/work/my-api" } }""");

        Assert.Equal("2", response.GetProperty("id").GetString());
        Assert.False(response.GetProperty("ok").GetBoolean());
        Assert.Equal(GitErrors.NotARepositoryCode, ErrorCode(response));
        Assert.Contains("/work/my-api", response.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task GitCommit_PassesTheMessageAndPaths()
    {
        var clean = new GitStatus("main", "origin/main", 1, 0, false, []);
        _git.GetStatusAsync(Workspace, Arg.Any<CancellationToken>()).Returns(
            Result<GitStatus>.Success(new GitStatus(
                "main",
                "origin/main",
                0,
                0,
                false,
                [new GitChange("a.yaml", GitFileStatus.Modified)])),
            Result<GitStatus>.Success(clean));
        _git.CommitAsync(default!, default!, default, default).ReturnsForAnyArgs(Result<string>.Success("abc123"));

        var response = await DispatchAsync(
            """
            { "id": "3", "type": "git.commit",
              "payload": { "workspacePath": "/work/my-api", "message": "Update A", "paths": ["a.yaml"] } }
            """);

        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        Assert.Equal("abc123", response.GetProperty("payload").GetProperty("commitId").GetString());
        _ = _git.Received(1).CommitAsync(
            Workspace,
            Arg.Is<CommitMessage>(m => m.Value == "Update A"),
            Arg.Is<IReadOnlyList<string>?>(p => p != null && p.SequenceEqual(new[] { "a.yaml" })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GitPull_ReturnsTheOutcome()
    {
        var status = new GitStatus("main", "origin/main", 0, 0, false, []);
        _git.GetStatusAsync(Workspace, Arg.Any<CancellationToken>()).Returns(Result<GitStatus>.Success(status));
        _git.PullAsync(Workspace, Arg.Any<CancellationToken>()).Returns(Result<PullOutcome>.Success(PullOutcome.UpToDate));

        var response = await DispatchAsync(
            """{ "id": "4", "type": "git.pull", "payload": { "workspacePath": "/work/my-api" } }""");

        Assert.Equal("upToDate", response.GetProperty("payload").GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task UnexpectedExceptions_AreReportedAsInternalErrors()
    {
        _git.GetStatusAsync(Workspace, Arg.Any<CancellationToken>()).ThrowsAsync(new IOException("disk on fire"));

        var response = await DispatchAsync(
            """{ "id": "5", "type": "git.push", "payload": { "workspacePath": "/work/my-api" } }""");

        Assert.Equal(BridgeErrors.InternalErrorCode, ErrorCode(response));
        Assert.Contains("disk on fire", response.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task PickFolder_ReturnsThePathAndName()
    {
        var folder = Path.Combine(Path.GetTempPath(), "my-api");
        _picker.PickFolderAsync(default!, default).ReturnsForAnyArgs(Task.FromResult<string?>(folder));

        var response = await DispatchAsync("""{ "id": "6", "type": "workspace.pickFolder", "payload": {} }""");

        var payload = response.GetProperty("payload");
        Assert.Equal(folder, payload.GetProperty("path").GetString());
        Assert.Equal("my-api", payload.GetProperty("name").GetString());
    }

    [Fact]
    public async Task PickFolder_Cancelled_ReturnsNoPath()
    {
        _picker.PickFolderAsync(default!, default).ReturnsForAnyArgs(Task.FromResult<string?>(null));

        var response = await DispatchAsync("""{ "id": "6", "type": "workspace.pickFolder", "payload": {} }""");

        Assert.True(response.GetProperty("ok").GetBoolean());
        Assert.False(response.GetProperty("payload").TryGetProperty("path", out _));
    }

    private async Task<JsonElement> DispatchAsync(string message)
    {
        var json = await _dispatcher.DispatchAsync(message);
        Assert.NotNull(json);
        return JsonDocument.Parse(json).RootElement;
    }

    private static string? ErrorCode(JsonElement response) =>
        response.GetProperty("error").GetProperty("code").GetString();
}
