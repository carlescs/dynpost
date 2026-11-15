using Dynpost.Application.Common;
using Dynpost.Application.Environments;
using Dynpost.Application.Folders;
using Dynpost.Application.Interchange;
using Dynpost.Application.Requests;
using Dynpost.Application.Workspaces;
using Dynpost.Domain.Environments;
using Dynpost.Domain.Requests;
using Dynpost.Domain.Workspaces;
using NSubstitute;

namespace Dynpost.Application.Tests.Interchange;

using Secrets = IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>;

public class CopyAsCurlTests
{
    private const string Root = "/work/api";
    private readonly ICurlConverter _converter = Substitute.For<ICurlConverter>();
    private readonly IEnvironmentStore _environments = Substitute.For<IEnvironmentStore>();
    private readonly ISecretStore _secrets = Substitute.For<ISecretStore>();
    private readonly ICapturedValueStore _captures = Substitute.For<ICapturedValueStore>();
    private readonly IWorkspaceStateStore _state = Substitute.For<IWorkspaceStateStore>();
    private readonly IFolderDefaultsStore _folders = Substitute.For<IFolderDefaultsStore>();
    private readonly TimeProvider _time = Substitute.For<TimeProvider>();
    private readonly CurrentWorkspace _current = new();
    private CurlCommand? _written;

    public CopyAsCurlTests()
    {
        _current.Open(Root);
        _time.GetUtcNow().Returns(DateTimeOffset.UnixEpoch);
        _folders.ReadAsync(Root, Arg.Any<WorkspacePath>(), Arg.Any<CancellationToken>()).Returns(FolderDefaults.Empty);
        _folders.ReadAsync(Root, WorkspacePath.Parse("requests"), Arg.Any<CancellationToken>())
            .Returns(new FolderDefaults([new KeyValueEntry("X-Team", "core")]));
        _state.LoadAsync(Root, Arg.Any<CancellationToken>()).Returns(new WorkspaceState("dev.yaml"));
        _environments.Exists(Root, "dev.yaml").Returns(true);
        _environments.ReadAsync(Root, "dev.yaml", Arg.Any<CancellationToken>()).Returns(new EnvironmentDefinition(
            "Dev",
            [new EnvironmentVariable("host", "api.test"), new EnvironmentVariable("token", "", Secret: true)]));
        _secrets.LoadAsync(Root, Arg.Any<CancellationToken>()).Returns(Result<Secrets>.Success(
            new Dictionary<string, IReadOnlyDictionary<string, string>> { ["dev.yaml"] = new Dictionary<string, string> { ["token"] = "s3cr3t" } }));
        _captures.LoadAsync(Root, Arg.Any<CancellationToken>()).Returns(Result<Secrets>.Success(new Dictionary<string, IReadOnlyDictionary<string, string>>()));
        _converter.Write(Arg.Do<CurlCommand>(c => _written = c), Arg.Any<CurlShell>()).Returns("curl …");
    }

    private CopyAsCurl Create() => new(
        _converter,
        new ActiveEnvironment(_environments, _secrets, _captures, _state, _current),
        new FolderInheritance(_folders),
        _current,
        _time);

    private static RequestDto Request() => new(
        "Get user",
        "GET",
        "https://{{host}}/users/:id",
        [new KeyValueDto("q", "{{token}}")],
        [],
        new AuthDto("bearer", Token: "{{token}}"),
        new BodyDto("none", ""),
        PathParams: [new KeyValueDto("id", "42")]);

    [Fact]
    public async Task Resolved_UsesTheEnvironmentAndFoldersAndMasksSecrets()
    {
        var result = await Create().ExecuteAsync(Request(), "requests/get-user.yaml", null, resolve: true, includeSecrets: false, CurlShell.Posix);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal("GET", _written!.Method);
        Assert.StartsWith("https://api.test/users/42?q=", _written.Url);
        Assert.DoesNotContain("s3cr3t", _written.Url);
        Assert.Contains(KeyValuePair.Create("X-Team", "core"), _written.Headers);
        Assert.Contains(_written.Headers, h => h.Key == "Authorization" && !h.Value.Contains("s3cr3t"));
    }

    [Fact]
    public async Task Resolved_WithSecrets_KeepsTheirValues()
    {
        await Create().ExecuteAsync(Request(), "requests/get-user.yaml", null, resolve: true, includeSecrets: true, CurlShell.Cmd);

        Assert.Equal("https://api.test/users/42?q=s3cr3t", _written!.Url);
        Assert.Contains(KeyValuePair.Create("Authorization", "Bearer s3cr3t"), _written.Headers);
        _converter.Received(1).Write(Arg.Any<CurlCommand>(), CurlShell.Cmd);
    }

    [Fact]
    public async Task AsWritten_KeepsTheVariables()
    {
        await Create().ExecuteAsync(Request(), null, "requests", resolve: false, includeSecrets: false, CurlShell.Posix);

        Assert.Equal("https://{{host}}/users/42?q={{token}}", _written!.Url);
        Assert.Contains(KeyValuePair.Create("Authorization", "Bearer {{token}}"), _written.Headers);
        Assert.Contains(KeyValuePair.Create("X-Team", "core"), _written.Headers);
    }
}
