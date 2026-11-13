using Dynpost.Domain.Git;
using Dynpost.Domain.Workspaces;
using Dynpost.Infrastructure.Git;
using Dynpost.Infrastructure.Sharing;
using Dynpost.Infrastructure.Tests.Git;

namespace Dynpost.Infrastructure.Tests.Sharing;

/// <summary>Local-only files against real temporary repositories: the .gitignore entry plus git's index.</summary>
public sealed class LocalOnlyGitTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly GitCliService _git = new();
    private readonly GitIgnoreLocalOnlyFiles _localFiles = new();
    private readonly string _workspace;

    public LocalOnlyGitTests()
    {
        _workspace = _temp.Combine("repo");
        Directory.CreateDirectory(_workspace);
        TestGit.Run(_workspace, "init", "--quiet", "--initial-branch=main");
        TestGit.Configure(_workspace);
        Write(".gitignore", ".dynpost/\n");
        Write("environments/dev.yaml", "name: Dev\n");
        Write("environments/local.yaml", "name: Local\n");
        Write("users/get-user.yaml", "name: Get user\n");
        TestGit.CommitAll(_workspace, "Initial");
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task MakingATrackedFileLocalOnly_ShowsItsRemoval_AndCommittingKeepsItOnDisk()
    {
        await MakeLocalOnlyAsync("environments/local.yaml");

        var status = (await _git.GetStatusAsync(_workspace)).Value;
        Assert.Equal(
            [new GitChange(".gitignore", GitFileStatus.Modified), new GitChange("environments/local.yaml", GitFileStatus.Deleted)],
            status.Changes.OrderBy(c => c.Path, StringComparer.Ordinal));

        var commit = await _git.CommitAsync(_workspace, Message("Keep local environment private"), paths: null);

        Assert.True(commit.IsSuccess, commit.Error?.Message);
        Assert.Equal(".gitignore\nenvironments/local.yaml\n", CommittedFiles());
        Assert.DoesNotContain("environments/local.yaml", TestGit.Run(_workspace, "ls-files"));
        Assert.Equal("name: Local\n", File.ReadAllText(Path.Combine(_workspace, "environments", "local.yaml")));
        Assert.Empty((await _git.GetStatusAsync(_workspace)).Value.Changes);
    }

    [Fact]
    public async Task CommittingSelectedPaths_IncludesTheRemoval_AndLeavesOtherStagedChanges()
    {
        await MakeLocalOnlyAsync("environments/local.yaml");
        Write("users/get-user.yaml", "name: Get user v2\n");
        Write("environments/dev.yaml", "name: Dev v2\n");
        TestGit.Run(_workspace, "add", "environments/dev.yaml");

        var commit = await _git.CommitAsync(
            _workspace,
            Message("Keep local environment private"),
            ["environments/local.yaml", ".gitignore", "users/get-user.yaml"]);

        Assert.True(commit.IsSuccess, commit.Error?.Message);
        Assert.Equal(".gitignore\nenvironments/local.yaml\nusers/get-user.yaml\n", CommittedFiles());
        Assert.Equal("name: Local\n", File.ReadAllText(Path.Combine(_workspace, "environments", "local.yaml")));
        var status = (await _git.GetStatusAsync(_workspace)).Value;
        Assert.Equal(["environments/dev.yaml"], status.Changes.Select(c => c.Path));
        Assert.Equal("M  environments/dev.yaml\n", TestGit.Run(_workspace, "status", "--porcelain"));
    }

    [Fact]
    public async Task CommittingOnlyTheRemoval_Works()
    {
        await MakeLocalOnlyAsync("users/get-user.yaml");

        var commit = await _git.CommitAsync(_workspace, Message("Stop sharing"), ["users/get-user.yaml"]);

        Assert.True(commit.IsSuccess, commit.Error?.Message);
        Assert.Equal("users/get-user.yaml\n", CommittedFiles());
        var status = (await _git.GetStatusAsync(_workspace)).Value;
        Assert.Equal([".gitignore"], status.Changes.Select(c => c.Path));
    }

    [Fact]
    public async Task CommittingOtherPaths_LeavesTheRemovalStaged()
    {
        await MakeLocalOnlyAsync("users/get-user.yaml");
        Write("environments/dev.yaml", "name: Dev v2\n");

        var commit = await _git.CommitAsync(_workspace, Message("Update dev"), ["environments/dev.yaml"]);

        Assert.True(commit.IsSuccess, commit.Error?.Message);
        Assert.Equal("environments/dev.yaml\n", CommittedFiles());
        Assert.Contains("D  users/get-user.yaml", TestGit.Run(_workspace, "status", "--porcelain"));
    }

    [Fact]
    public async Task SharingItAgainBeforeCommitting_UndoesTheChange()
    {
        await MakeLocalOnlyAsync("environments/local.yaml");

        Assert.True(_localFiles.Set(_workspace, P("environments/local.yaml"), localOnly: false).Value);
        var restored = await _git.RestoreTrackingAsync(_workspace, "environments/local.yaml");

        Assert.True(restored.IsSuccess, restored.Error?.Message);
        Assert.Equal(".dynpost/\n", File.ReadAllText(Path.Combine(_workspace, ".gitignore")));
        Assert.Empty((await _git.GetStatusAsync(_workspace)).Value.Changes);
    }

    [Fact]
    public async Task ANewFileMadeLocalOnly_NeverShowsUp()
    {
        Write("environments/mine.yaml", "name: Mine\n");

        await MakeLocalOnlyAsync("environments/mine.yaml");

        var status = (await _git.GetStatusAsync(_workspace)).Value;
        Assert.Equal([".gitignore"], status.Changes.Select(c => c.Path));
        Assert.Equal([P("environments/mine.yaml")], _localFiles.List(_workspace).Value);
    }

    [Fact]
    public async Task StopTracking_OutsideARepository_IsReported()
    {
        var folder = _temp.Combine("plain");
        Directory.CreateDirectory(folder);

        var result = await _git.StopTrackingAsync(folder, "a.yaml");

        Assert.Equal(Dynpost.Application.Git.GitErrors.NotARepositoryCode, result.Error?.Code);
    }

    [Fact]
    public void MoveCopyAndRemove_FollowTheFiles()
    {
        Assert.True(_localFiles.Set(_workspace, P("users/get-user.yaml"), localOnly: true).Value);

        Assert.True(_localFiles.Move(_workspace, P("users"), P("people")).IsSuccess);
        Assert.True(_localFiles.Copy(_workspace, P("people/get-user.yaml"), P("people/get-user-copy.yaml")).IsSuccess);
        Assert.Equal(
            ".dynpost/\n/people/get-user.yaml\n/people/get-user-copy.yaml\n",
            File.ReadAllText(Path.Combine(_workspace, ".gitignore")));

        Assert.True(_localFiles.Remove(_workspace, P("people")).IsSuccess);
        Assert.Equal(".dynpost/\n", File.ReadAllText(Path.Combine(_workspace, ".gitignore")));
    }

    [Fact]
    public void Set_AddsTheLocalFolderEntryToo_AndWritesOnlyWhenChanged()
    {
        var folder = _temp.Combine("fresh");
        Directory.CreateDirectory(folder);

        Assert.True(_localFiles.Set(folder, P("a.yaml"), localOnly: true).Value);
        Assert.False(_localFiles.Set(folder, P("a.yaml"), localOnly: true).Value);
        Assert.Equal(".dynpost/\n/a.yaml\n", File.ReadAllText(Path.Combine(folder, ".gitignore")));
        Assert.True(_localFiles.Remove(folder, P("missing.yaml")).IsSuccess);
        Assert.Empty(_localFiles.List(_temp.Combine("nothing-here")).Value);
    }

    private async Task MakeLocalOnlyAsync(string path)
    {
        Assert.True(_localFiles.Set(_workspace, P(path), localOnly: true).IsSuccess);
        var stopped = await _git.StopTrackingAsync(_workspace, path);
        Assert.True(stopped.IsSuccess, stopped.Error?.Message);
    }

    private void Write(string relativePath, string content)
    {
        var path = Path.Combine(_workspace, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private string CommittedFiles() =>
        TestGit.Run(_workspace, "show", "--name-only", "--format=", "HEAD").ReplaceLineEndings("\n").TrimStart('\n');

    private static WorkspacePath P(string path) => WorkspacePath.Parse(path);

    private static CommitMessage Message(string text) =>
        CommitMessage.TryCreate(text, out var message) ? message : throw new ArgumentException(text);
}
