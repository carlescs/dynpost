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
}
