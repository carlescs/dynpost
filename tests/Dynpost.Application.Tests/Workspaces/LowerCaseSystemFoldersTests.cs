using Dynpost.Application.Common;
using Dynpost.Application.Git;
using Dynpost.Application.Workspaces;
using NSubstitute;

namespace Dynpost.Application.Tests.Workspaces;

public class LowerCaseSystemFoldersTests
{
    private const string Root = "/work/api";

    private readonly IWorkspaceLayoutStore _layout = Substitute.For<IWorkspaceLayoutStore>();
    private readonly IGitService _git = Substitute.For<IGitService>();

    public LowerCaseSystemFoldersTests() =>
        _layout.RenameRootFolder(Root, Arg.Any<string>(), Arg.Any<string>()).Returns(Unit.Value);

    private LowerCaseSystemFolders UseCase() => new(_layout, _git);

    private void Found(params MiscasedSystemFolder[] folders) =>
        _layout.FindMiscasedSystemFolders(Root).Returns(Result<IReadOnlyList<MiscasedSystemFolder>>.Success(folders));

    [Fact]
    public async Task RenamesTrackedFoldersWithGit_AndOthersOnDisk()
    {
        Found(new MiscasedSystemFolder("Requests", "requests"), new MiscasedSystemFolder("PLANS", "plans"));
        _git.MoveTrackedFolderAsync(Root, "Requests", "requests", Arg.Any<CancellationToken>()).Returns(true);
        _git.MoveTrackedFolderAsync(Root, "PLANS", "plans", Arg.Any<CancellationToken>()).Returns(false);

        var renamed = await UseCase().ExecuteAsync(Root);

        Assert.Equal(["Requests", "PLANS"], renamed);
        _layout.DidNotReceive().RenameRootFolder(Root, "Requests", Arg.Any<string>());
        _layout.Received(1).RenameRootFolder(Root, "PLANS", "plans");
    }

    [Fact]
    public async Task RenamesOnDisk_WhenGitIsNotInstalled()
    {
        Found(new MiscasedSystemFolder("Requests", "requests"));
        _git.MoveTrackedFolderAsync(Root, "Requests", "requests", Arg.Any<CancellationToken>())
            .Returns(GitErrors.GitNotInstalled());

        Assert.Equal(["Requests"], await UseCase().ExecuteAsync(Root));

        _layout.Received(1).RenameRootFolder(Root, "Requests", "requests");
    }

    [Fact]
    public async Task LeavesTheFolder_WhenGitTracksItButCannotRenameIt()
    {
        // Say, during a merge: renaming on disk alone would hide the rename from git.
        Found(new MiscasedSystemFolder("Requests", "requests"));
        _git.MoveTrackedFolderAsync(Root, "Requests", "requests", Arg.Any<CancellationToken>())
            .Returns(new Error("git.failed", "you need to resolve your current index first"));

        Assert.Empty(await UseCase().ExecuteAsync(Root));

        _layout.DidNotReceiveWithAnyArgs().RenameRootFolder(default!, default!, default!);
    }

    [Fact]
    public async Task RenamesTheLocalFolderWithoutGit()
    {
        Found(new MiscasedSystemFolder(".Dynpost", ".dynpost"));

        await UseCase().ExecuteAsync(Root);

        await _git.DidNotReceiveWithAnyArgs().MoveTrackedFolderAsync(default!, default!, default!, default);
        _layout.Received(1).RenameRootFolder(Root, ".Dynpost", ".dynpost");
    }

    [Theory]
    [InlineData("Requests/health.yaml", true)]
    [InlineData("ENVIRONMENTS/dev.yaml", true)]
    [InlineData(".Dynpost", true)]
    [InlineData("requests/health.yaml", false)]
    [InlineData("Requests.md", false)]
    [InlineData("docs/Requests/a.yaml", false)]
    public void MayBeNeeded_ForChangesInAMiscasedSystemFolder(string file, bool expected) =>
        Assert.Equal(expected, LowerCaseSystemFolders.MayBeNeeded([file]));

    [Fact]
    public void MayBeNeeded_WhenChangesWereLost() => Assert.True(LowerCaseSystemFolders.MayBeNeeded(null));
}
