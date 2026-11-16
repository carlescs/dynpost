using Dynpost.Application.Certificates;
using Dynpost.Application.Common;
using Dynpost.Application.Environments;
using Dynpost.Application.Git;
using Dynpost.Application.Secrets;
using Dynpost.Application.Workspaces;
using Dynpost.Domain.Git;
using Dynpost.Domain.Http;
using DomainPath = Dynpost.Domain.Workspaces.WorkspacePath;
using NSubstitute;
using static Dynpost.Application.Tests.Git.GitTestData;

namespace Dynpost.Application.Tests.Git;

public class WorktreeUseCasesTests
{
    private const string Parent = "/work";
    private const string NewFolder = "/work/my-api-feature";

    private readonly IGitService _git = Substitute.For<IGitService>();
    private readonly CurrentWorkspace _workspace = OpenedWorkspace();
    private readonly IWorkspaceRepository _repository = Substitute.For<IWorkspaceRepository>();
    private readonly ISecretStore _secrets = Substitute.For<ISecretStore>();
    private readonly IClientCertificateStore _certificates = Substitute.For<IClientCertificateStore>();
    private readonly IDotEnvValues _dotEnv = Substitute.For<IDotEnvValues>();

    private static readonly GitWorktree Main = new(WorkspacePath, WorkspacePath, "main", "a1", IsMain: true, IsCurrent: true);
    private static readonly GitWorktree Feature = new(NewFolder, NewFolder, "feature", "b2", IsMain: false, IsCurrent: false);

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Secrets =
        new Dictionary<string, IReadOnlyDictionary<string, string>> { ["dev"] = new Dictionary<string, string> { ["token"] = "s3cret" } };

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> NoSecrets =
        new Dictionary<string, IReadOnlyDictionary<string, string>>();

    public WorktreeUseCasesTests()
    {
        _git.GetStatusAsync(WorkspacePath, Arg.Any<CancellationToken>()).Returns(Ok(Clean()));
        _git.ListBranchesAsync(WorkspacePath, Arg.Any<CancellationToken>()).Returns(Ok<IReadOnlyList<GitBranch>>([
            new GitBranch("main", null, true, null, "a1", null, "Init"),
            new GitBranch("feature", null, false, null, "b2", null, "Work"),
        ]));
        _git.AddWorktreeAsync(default!, default!, default!, default).ReturnsForAnyArgs(Ok(Unit.Value));
        _git.RemoveWorktreeAsync(default!, default!, default).ReturnsForAnyArgs(Ok(Unit.Value));
        _git.PruneWorktreesAsync(default!).ReturnsForAnyArgs(Ok(Unit.Value));
        HasWorktrees(Main, Feature);
        _repository.FolderExists(Arg.Any<string>()).Returns(true);
        _secrets.LoadAsync(WorkspacePath, Arg.Any<CancellationToken>()).Returns(Ok(Secrets));
        _secrets.LoadAsync(NewFolder, Arg.Any<CancellationToken>()).Returns(Ok(NoSecrets));
        _secrets.SaveAsync(default!, default!).ReturnsForAnyArgs(Ok(Unit.Value));
        _certificates.LoadAsync(default!).ReturnsForAnyArgs(
            Ok<IReadOnlyDictionary<string, IReadOnlyList<ClientCertificateSetting>>>(
                new Dictionary<string, IReadOnlyList<ClientCertificateSetting>>()));
        _certificates.SaveAsync(default!, default!).ReturnsForAnyArgs(Ok(Unit.Value));
        _dotEnv.CopyAsync(default!, default!).ReturnsForAnyArgs(Ok(true));
    }

    private void HasWorktrees(params GitWorktree[] worktrees) =>
        _git.ListWorktreesAsync(WorkspacePath, Arg.Any<CancellationToken>()).Returns(Ok<IReadOnlyList<GitWorktree>>(worktrees));

    private CreateWorktree Create() => new(_git, _workspace, _repository, _secrets, _certificates, _dotEnv);

    private RemoveWorktree Remove() => new(_git, _workspace, _repository, _secrets);

    [Fact]
    public async Task List_ReturnsTheWorktrees()
    {
        var result = await new ListWorktrees(_git, _workspace).ExecuteAsync();

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal([Main, Feature], result.Value);
    }

    [Fact]
    public async Task List_WithoutAWorkspace_IsRejected()
    {
        var result = await new ListWorktrees(_git, new CurrentWorkspace()).ExecuteAsync();

        Assert.Equal(ErrorCodes.NoWorkspace, result.Error?.Code);
    }

    [Fact]
    public async Task Create_ChecksOutAnExistingBranch_AndCopiesSecretsAndTheDotEnvFile()
    {
        var result = await Create().ExecuteAsync(new CreateWorktreeRequest(Parent, " my-api-feature ", " feature "));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(Feature, result.Value.Worktree);
        Assert.Null(result.Value.Warning);
        await _git.Received(1).AddWorktreeAsync(WorkspacePath, Path.Combine(Parent, "my-api-feature"), "feature", false, Arg.Any<CancellationToken>());
        await _secrets.Received(1).SaveAsync(NewFolder, Secrets, Arg.Any<CancellationToken>());
        await _dotEnv.Received(1).CopyAsync(WorkspacePath, NewFolder, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_WithANewBranch_CreatesIt()
    {
        HasWorktrees(Main, Feature with { Branch = "login" });

        var result = await Create().ExecuteAsync(new CreateWorktreeRequest(Parent, "my-api-login", "login", CreateBranch: true));

        Assert.True(result.IsSuccess, result.Error?.Message);
        await _git.Received(1).AddWorktreeAsync(WorkspacePath, Path.Combine(Parent, "my-api-login"), "login", true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_PointsCertificateFilesGitDoesNotCarryAtTheOriginals()
    {
        var setting = new ClientCertificateSetting("api.example.com", "certs/tracked.pem", "certs/local.key", "pass");
        _certificates.LoadAsync(WorkspacePath, Arg.Any<CancellationToken>()).Returns(
            Ok<IReadOnlyDictionary<string, IReadOnlyList<ClientCertificateSetting>>>(
                new Dictionary<string, IReadOnlyList<ClientCertificateSetting>> { ["dev"] = [setting] }));
        _repository.FileExists(NewFolder, DomainPath.Parse("certs/tracked.pem")).Returns(true);
        _repository.FileExists(NewFolder, DomainPath.Parse("certs/local.key")).Returns(false);

        await Create().ExecuteAsync(new CreateWorktreeRequest(Parent, "my-api-feature", "feature"));

        var expected = setting with { Key = Path.GetFullPath(Path.Combine(WorkspacePath, "certs/local.key")) };
        await _certificates.Received(1).SaveAsync(
            NewFolder,
            Arg.Is<IReadOnlyDictionary<string, IReadOnlyList<ClientCertificateSetting>>>(c => c["dev"].Single() == expected),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_ReportsLocalDataItCouldNotCopy_ButKeepsTheWorktree()
    {
        _secrets.SaveAsync(NewFolder, Arg.Any<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>>(), Arg.Any<CancellationToken>())
            .Returns(new Error(ErrorCodes.Io, "keychain locked"));

        var result = await Create().ExecuteAsync(new CreateWorktreeRequest(Parent, "my-api-feature", "feature"));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Contains("secret values (keychain locked)", result.Value.Warning);
    }

    [Fact]
    public async Task Create_ForABranchWithoutTheWorkspaceFolder_WarnsAndCopiesNothing()
    {
        _repository.FolderExists(NewFolder).Returns(false);

        var result = await Create().ExecuteAsync(new CreateWorktreeRequest(Parent, "my-api-feature", "feature"));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Contains("nothing to open", result.Value.Warning);
        await _secrets.DidNotReceiveWithAnyArgs().SaveAsync(default!, default!);
    }

    public static TheoryData<CreateWorktreeRequest, string> CreateRefusals() => new()
    {
        { new CreateWorktreeRequest(Parent, "a/b", "feature"), GitErrors.InvalidWorktreeFolderCode },
        { new CreateWorktreeRequest(Parent, " ", "feature"), GitErrors.InvalidWorktreeFolderCode },
        { new CreateWorktreeRequest(Parent, "x", "nope"), GitErrors.BranchNotFoundCode },
        { new CreateWorktreeRequest(Parent, "x", "Feature", CreateBranch: true), GitErrors.BranchExistsCode },
        { new CreateWorktreeRequest(Parent, "x", "bad name", CreateBranch: true), GitErrors.InvalidBranchNameCode },
    };

    [Theory]
    [MemberData(nameof(CreateRefusals))]
    public async Task Create_InvalidRequests_AreRefused(CreateWorktreeRequest request, string code)
    {
        var result = await Create().ExecuteAsync(request);

        Assert.Equal(code, result.Error?.Code);
        await _git.DidNotReceiveWithAnyArgs().AddWorktreeAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task Create_InAMissingParentFolder_IsRefused()
    {
        _repository.FolderExists("/nowhere").Returns(false);

        var result = await Create().ExecuteAsync(new CreateWorktreeRequest("/nowhere", "x", "feature"));

        Assert.Equal(ErrorCodes.NotFound, result.Error?.Code);
    }

    [Fact]
    public async Task Create_BeforeTheFirstCommit_IsRefused()
    {
        _git.GetStatusAsync(WorkspacePath, Arg.Any<CancellationToken>()).Returns(Ok(Clean() with { IsUnborn = true }));

        var result = await Create().ExecuteAsync(new CreateWorktreeRequest(Parent, "x", "y", CreateBranch: true));

        Assert.Equal(GitErrors.NoCommitsCode, result.Error?.Code);
    }

    [Fact]
    public async Task Create_PassesOnGitRefusals_WithoutCopyingAnything()
    {
        _git.AddWorktreeAsync(default!, default!, default!, default)
            .ReturnsForAnyArgs(GitErrors.BranchInWorktree("feature", "/elsewhere"));

        var result = await Create().ExecuteAsync(new CreateWorktreeRequest(Parent, "x", "feature"));

        Assert.Equal(GitErrors.BranchInWorktreeCode, result.Error?.Code);
        await _secrets.DidNotReceiveWithAnyArgs().SaveAsync(default!, default!);
    }

    [Fact]
    public async Task Remove_DeletesItsSecretsThenTheWorktree()
    {
        _secrets.LoadAsync(NewFolder, Arg.Any<CancellationToken>()).Returns(Ok(Secrets));

        var result = await Remove().ExecuteAsync(new RemoveWorktreeRequest(NewFolder));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Received.InOrder(() =>
        {
            _secrets.SaveAsync(NewFolder, Arg.Is<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>>(s => s.Count == 0), Arg.Any<CancellationToken>());
            _git.RemoveWorktreeAsync(WorkspacePath, Feature, false, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Remove_ThatGitRefuses_PutsTheSecretsBack()
    {
        _secrets.LoadAsync(NewFolder, Arg.Any<CancellationToken>()).Returns(Ok(Secrets));
        _git.RemoveWorktreeAsync(default!, default!, default).ReturnsForAnyArgs(GitErrors.WorktreeHasChanges(NewFolder));

        var result = await Remove().ExecuteAsync(new RemoveWorktreeRequest(NewFolder));

        Assert.Equal(GitErrors.WorktreeHasChangesCode, result.Error?.Code);
        await _secrets.Received(1).SaveAsync(NewFolder, Secrets, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Remove_PassesForce()
    {
        await Remove().ExecuteAsync(new RemoveWorktreeRequest(NewFolder, Force: true));

        await _git.Received(1).RemoveWorktreeAsync(WorkspacePath, Feature, true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Remove_AWorktreeWhoseFolderIsGone_PrunesIt()
    {
        HasWorktrees(Main, Feature with { IsPrunable = true });

        var result = await Remove().ExecuteAsync(new RemoveWorktreeRequest(NewFolder));

        Assert.True(result.IsSuccess, result.Error?.Message);
        await _git.Received(1).PruneWorktreesAsync(WorkspacePath, Arg.Any<CancellationToken>());
        await _git.DidNotReceiveWithAnyArgs().RemoveWorktreeAsync(default!, default!, default);
    }

    public static TheoryData<GitWorktree, string> RemoveRefusals() => new()
    {
        { Main with { IsCurrent = false }, GitErrors.WorktreeInUseCode },
        { Feature with { IsCurrent = true }, GitErrors.WorktreeInUseCode },
        { Feature with { IsLocked = true }, GitErrors.WorktreeLockedCode },
    };

    [Theory]
    [MemberData(nameof(RemoveRefusals))]
    public async Task Remove_TheMainOpenOrALockedWorktree_IsRefused(GitWorktree worktree, string code)
    {
        HasWorktrees(worktree);

        var result = await Remove().ExecuteAsync(new RemoveWorktreeRequest(worktree.Path));

        Assert.Equal(code, result.Error?.Code);
        await _git.DidNotReceiveWithAnyArgs().RemoveWorktreeAsync(default!, default!, default);
        await _secrets.DidNotReceiveWithAnyArgs().SaveAsync(default!, default!);
    }

    [Fact]
    public async Task Remove_AnUnlistedFolder_IsRefused()
    {
        var result = await Remove().ExecuteAsync(new RemoveWorktreeRequest("/home/me"));

        Assert.Equal(GitErrors.WorktreeNotFoundCode, result.Error?.Code);
        await _git.DidNotReceiveWithAnyArgs().RemoveWorktreeAsync(default!, default!, default);
    }
}
