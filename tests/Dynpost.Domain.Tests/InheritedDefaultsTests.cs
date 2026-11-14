using Dynpost.Domain.Requests;
using Dynpost.Domain.Workspaces;

namespace Dynpost.Domain.Tests;

public class InheritedDefaultsTests
{
    private static readonly WorkspacePath Requests = WorkspacePath.Parse("requests");
    private static readonly WorkspacePath Users = WorkspacePath.Parse("requests/users");
    private static readonly WorkspacePath Admin = WorkspacePath.Parse("requests/users/admin");

    private static HttpRequestDefinition Request(RequestAuth? auth = null, params KeyValueEntry[] headers) =>
        new("Get user", RequestMethod.Get, "https://api.test/users/1", headers: headers, auth: auth);

    [Fact]
    public void ANewRequestInheritsItsAuth()
    {
        Assert.Same(RequestAuth.Inherit, Request().Auth);
        Assert.True(new FolderDefaults().IsEmpty);
    }

    [Fact]
    public void From_LetsCloserFoldersReplaceHeadersOfTheSameNameIgnoringCase()
    {
        var inherited = InheritedDefaults.From([
            (Requests, new FolderDefaults([new KeyValueEntry("Accept", "*/*"), new KeyValueEntry("X-Team", "core")])),
            (Users, new FolderDefaults([new KeyValueEntry("accept", "application/json")])),
        ]);

        Assert.Equal(
            [new InheritedHeader("X-Team", "core", Requests), new InheritedHeader("accept", "application/json", Users)],
            inherited.Headers);
    }

    [Fact]
    public void From_IgnoresDisabledHeaders()
    {
        var inherited = InheritedDefaults.From([
            (Requests, new FolderDefaults([new KeyValueEntry("Accept", "*/*")])),
            (Users, new FolderDefaults([new KeyValueEntry("Accept", "text/plain", Enabled: false)])),
        ]);

        Assert.Equal([new InheritedHeader("Accept", "*/*", Requests)], inherited.Headers);
    }

    [Fact]
    public void From_TakesTheAuthOfTheClosestFolderThatSetsOne()
    {
        var inherited = InheritedDefaults.From([
            (Requests, new FolderDefaults(auth: new BearerAuth("{{token}}"))),
            (Users, new FolderDefaults(auth: new BasicAuth("ada", "secret"))),
            (Admin, FolderDefaults.Empty),
        ]);

        Assert.Equal(new BasicAuth("ada", "secret"), inherited.Auth);
        Assert.Equal(Users, inherited.AuthFolder);
    }

    [Fact]
    public void From_ASubFolderCanTurnInheritedAuthOff()
    {
        var inherited = InheritedDefaults.From([
            (Requests, new FolderDefaults(auth: new BearerAuth("{{token}}"))),
            (Users, new FolderDefaults(auth: RequestAuth.None)),
        ]);

        Assert.Same(RequestAuth.None, inherited.EffectiveAuth);
        Assert.Equal(Users, inherited.AuthFolder);
    }

    [Fact]
    public void Apply_PutsInheritedHeadersFirstAndLetsTheRequestReplaceThem()
    {
        var inherited = InheritedDefaults.From([
            (Users, new FolderDefaults([new KeyValueEntry("Accept", "*/*"), new KeyValueEntry("X-Team", "core")])),
        ]);

        var applied = inherited.Apply(Request(null, new KeyValueEntry("ACCEPT", "application/json"), new KeyValueEntry("X-Debug", "1", Enabled: false)));

        Assert.Equal(
            [new KeyValueEntry("X-Team", "core"), new KeyValueEntry("ACCEPT", "application/json"), new KeyValueEntry("X-Debug", "1", Enabled: false)],
            applied.Headers);
    }

    [Fact]
    public void Apply_ADisabledRequestHeaderDoesNotReplaceTheInheritedOne()
    {
        var inherited = InheritedDefaults.From([(Users, new FolderDefaults([new KeyValueEntry("Accept", "*/*")]))]);

        var applied = inherited.Apply(Request(null, new KeyValueEntry("Accept", "text/plain", Enabled: false)));

        Assert.Equal([new KeyValueEntry("Accept", "*/*"), new KeyValueEntry("Accept", "text/plain", Enabled: false)], applied.Headers);
    }

    [Fact]
    public void Apply_UsesTheInheritedAuthOnlyWhenTheRequestInheritsIt()
    {
        var inherited = InheritedDefaults.From([(Users, new FolderDefaults(auth: new BearerAuth("{{token}}")))]);

        Assert.Equal(new BearerAuth("{{token}}"), inherited.Apply(Request()).Auth);
        Assert.Equal(new ApiKeyAuth("X-Key", "k"), inherited.Apply(Request(new ApiKeyAuth("X-Key", "k"))).Auth);
        Assert.Same(RequestAuth.None, inherited.Apply(Request(RequestAuth.None)).Auth);
    }

    [Fact]
    public void Apply_WithoutAnyFolderAuth_SendsNoAuth()
    {
        Assert.Same(RequestAuth.None, InheritedDefaults.None.Apply(Request()).Auth);
    }

    [Fact]
    public void Validate_ChecksHeaderNames()
    {
        var defaults = new FolderDefaults([new KeyValueEntry("Accept", "a"), new KeyValueEntry("accept", "b"), new KeyValueEntry("", "c")]);

        Assert.Equal(["The header 'accept' is defined more than once.", "Every header needs a name."], defaults.Validate());
    }

    [Fact]
    public void From_LetsCloserFoldersReplaceVariablesOfTheSameName()
    {
        var inherited = InheritedDefaults.From([
            (Requests, new FolderDefaults(variables: [new KeyValueEntry("page", "1"), new KeyValueEntry("size", "10"), new KeyValueEntry("Page", "x")])),
            (Users, new FolderDefaults(variables: [new KeyValueEntry("page", "2"), new KeyValueEntry("size", "50", Enabled: false)])),
        ]);

        Assert.Equal(
            [new InheritedVariable("size", "10", Requests), new InheritedVariable("Page", "x", Requests), new InheritedVariable("page", "2", Users)],
            inherited.Variables);
    }

    [Fact]
    public void ResolveVariables_AddsFolderVariablesTheEnvironmentLacks_TheEnvironmentWinning()
    {
        var inherited = InheritedDefaults.From([
            (Requests, new FolderDefaults(variables: [new KeyValueEntry("host", "folder.test"), new KeyValueEntry("usersUrl", "{{baseUrl}}/users")])),
            (Users, new FolderDefaults(variables: [new KeyValueEntry("adminUrl", "{{usersUrl}}/admin")])),
        ]);

        var resolved = inherited.ResolveVariables(new Dictionary<string, string> { ["host"] = "env.test", ["baseUrl"] = "https://env.test" });

        Assert.Empty(resolved.Cycle);
        Assert.Equal("env.test", resolved.Values["host"]);
        Assert.Equal("https://env.test/users", resolved.Values["usersUrl"]);
        Assert.Equal("https://env.test/users/admin", resolved.Values["adminUrl"]);
    }

    [Fact]
    public void ResolveVariables_DoesNotExpandEnvironmentValuesAgain()
    {
        var inherited = InheritedDefaults.From([(Requests, new FolderDefaults(variables: [new KeyValueEntry("echo", "<{{captured}}>"), new KeyValueEntry("x", "folder")]))]);

        var resolved = inherited.ResolveVariables(new Dictionary<string, string> { ["captured"] = "{{x}}" });

        Assert.Equal("<{{x}}>", resolved.Values["echo"]);
    }

    [Fact]
    public void ResolveVariables_LeavesUnknownReferencesAndReportsLoops()
    {
        var inherited = InheritedDefaults.From([
            (Requests, new FolderDefaults(variables: [new KeyValueEntry("a", "{{b}}"), new KeyValueEntry("unknown", "{{nope}}")])),
            (Users, new FolderDefaults(variables: [new KeyValueEntry("b", "{{a}}")])),
        ]);

        var resolved = inherited.ResolveVariables(new Dictionary<string, string>());

        Assert.Equal(["a", "b"], resolved.Cycle);
        Assert.Equal("{{nope}}", resolved.Values["unknown"]);
        Assert.False(resolved.Values.ContainsKey("a"));
        Assert.Equal([Requests, Users], inherited.FoldersOf(resolved.Cycle));
    }

    [Fact]
    public void ResolveVariables_WithoutFolderVariables_ReturnsTheEnvironment()
    {
        var environment = new Dictionary<string, string> { ["a"] = "1" };

        Assert.Same(environment, InheritedDefaults.None.ResolveVariables(environment).Values);
    }

    [Fact]
    public void FolderDefaults_WithOnlyVariables_IsNotEmpty_AndValidatesTheirNames()
    {
        var defaults = new FolderDefaults(variables: [new KeyValueEntry("ok", "1"), new KeyValueEntry("ok", "2"), new KeyValueEntry("bad name", "3"), new KeyValueEntry("", "4")]);

        Assert.False(defaults.IsEmpty);
        Assert.Equal(
            [
                "The variable 'ok' is defined more than once.",
                "'bad name' is not a valid variable name: it can't contain spaces or braces.",
                "Every variable needs a name.",
            ],
            defaults.Validate());
    }
}
