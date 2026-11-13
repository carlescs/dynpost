using Dynpost.Domain.Workspaces;
using Dynpost.Infrastructure.Sharing;

namespace Dynpost.Infrastructure.Tests.Sharing;

public sealed class GitIgnoreEntriesTests
{
    private static WorkspacePath P(string path) => WorkspacePath.Parse(path);

    [Theory]
    [InlineData("environments/local.yaml", "/environments/local.yaml")]
    [InlineData("users/get [draft]*.yaml", "/users/get \\[draft]\\*.yaml")]
    [InlineData("odd/name?.yaml", "/odd/name\\?.yaml")]
    [InlineData("#notes/a.yaml", "/#notes/a.yaml")]
    public void Format_AnchorsAndEscapesThePath(string path, string line)
    {
        Assert.Equal(line, GitIgnoreEntries.Format(P(path)));
        Assert.Equal(P(path), GitIgnoreEntries.Parse(line));
    }

    [Theory]
    [InlineData(".dynpost/")]
    [InlineData("*.log")]
    [InlineData("local.yaml")]
    [InlineData("/build/")]
    [InlineData("/users/*.yaml")]
    [InlineData("!/users/a.yaml")]
    [InlineData("# /users/a.yaml")]
    [InlineData("")]
    public void Parse_IgnoresPatternsThatAreNotOneAnchoredFile(string line)
    {
        Assert.Null(GitIgnoreEntries.Parse(line));
    }

    [Fact]
    public void Add_AppendsOneLineMatchingTheLineEndings()
    {
        Assert.Equal(".dynpost/\n/environments/local.yaml\n", GitIgnoreEntries.Add(".dynpost/\n", P("environments/local.yaml")));
        Assert.Equal(".dynpost/\r\n/a.yaml\r\n", GitIgnoreEntries.Add(".dynpost/\r\n", P("a.yaml")));
        Assert.Equal("*.log\n/a.yaml\n", GitIgnoreEntries.Add("*.log", P("a.yaml")));
        Assert.Equal("/a.yaml\n", GitIgnoreEntries.Add(string.Empty, P("a.yaml")));
    }

    [Fact]
    public void Add_LeavesAnExistingEntry()
    {
        const string content = "/a.yaml\n.dynpost/\n";

        Assert.Same(content, GitIgnoreEntries.Add(content, P("a.yaml")));
    }

    [Fact]
    public void Remove_DropsOnlyThatFilesLine()
    {
        Assert.Equal(".dynpost/\n*.log\n", GitIgnoreEntries.Remove(".dynpost/\n/a.yaml\n*.log\n", P("a.yaml")));
        Assert.Equal(".dynpost/\r\n", GitIgnoreEntries.Remove(".dynpost/\r\n/a.yaml\r\n", P("a.yaml")));
        Assert.Equal(string.Empty, GitIgnoreEntries.Remove("/a.yaml\n", P("a.yaml")));
    }

    [Fact]
    public void Remove_AFolderDropsTheEntriesInsideIt()
    {
        var content = "/users/a.yaml\n/users/admin/b.yaml\n/users-old/c.yaml\n";

        Assert.Equal("/users-old/c.yaml\n", GitIgnoreEntries.Remove(content, P("users")));
    }

    [Fact]
    public void Remove_WithoutAnEntry_ReturnsTheSameContent()
    {
        const string content = ".dynpost/\n/b.yaml";

        Assert.Same(content, GitIgnoreEntries.Remove(content, P("a.yaml")));
    }

    [Fact]
    public void Move_RewritesTheEntriesInPlace()
    {
        var content = ".dynpost/\n/users/a.yaml\n*.log\n/users/admin/b.yaml\n/other.yaml\n";

        Assert.Equal(
            ".dynpost/\n/people/a.yaml\n*.log\n/people/admin/b.yaml\n/other.yaml\n",
            GitIgnoreEntries.Move(content, P("users"), P("people")));
        Assert.Equal(
            ".dynpost/\n/users/c.yaml\n*.log\n/users/admin/b.yaml\n/other.yaml\n",
            GitIgnoreEntries.Move(content, P("users/a.yaml"), P("users/c.yaml")));
    }

    [Fact]
    public void Copy_AddsEntriesForTheCopies()
    {
        var content = "/users/a.yaml\n/users/b.yaml\n/orders/c.yaml\n";

        Assert.Equal(
            content + "/users copy/a.yaml\n/users copy/b.yaml\n",
            GitIgnoreEntries.Copy(content, P("users"), P("users copy")));
        Assert.Same(content, GitIgnoreEntries.Copy(content, P("orders/d.yaml"), P("orders/d-copy.yaml")));
    }

    [Fact]
    public void List_FindsEveryLocalOnlyFile()
    {
        var listed = GitIgnoreEntries.List(".dynpost/\n/environments/local.yaml\r\n*.log\n/users/a.yaml\n");

        Assert.Equal([P("environments/local.yaml"), P("users/a.yaml")], listed.OrderBy(p => p.Value));
    }
}
