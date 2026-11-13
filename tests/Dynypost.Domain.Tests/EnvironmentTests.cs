using Dynypost.Domain.Environments;
using Dynypost.Domain.Requests;
using Dynypost.Domain.Workspaces;

namespace Dynypost.Domain.Tests;

public class EnvironmentTests
{
    private static EnvironmentVariable Var(string name, string value) => new(name, value);

    [Fact]
    public void Resolve_ExpandsVariablesThatUseOtherVariables()
    {
        var resolved = VariableSet.Resolve([
            Var("baseUrl", "{{scheme}}://{{host}}/api"),
            Var("host", "{{name}}.example.com"),
            Var("name", "dev"),
            Var("scheme", "https"),
        ]);

        Assert.Empty(resolved.Cycle);
        Assert.Equal("https://dev.example.com/api", resolved.Values["baseUrl"]);
        Assert.Equal("dev.example.com", resolved.Values["host"]);
    }

    [Fact]
    public void Resolve_LeavesUnknownReferencesAsWritten()
    {
        var resolved = VariableSet.Resolve([Var("url", "{{ host }}/x")]);

        Assert.Equal("{{ host }}/x", resolved.Values["url"]);
    }

    [Fact]
    public void Resolve_ReportsLoopsButNotTheVariablesThatOnlyUseThem()
    {
        var resolved = VariableSet.Resolve([
            Var("a", "{{b}}"),
            Var("b", "x{{c}}"),
            Var("c", "{{a}}"),
            Var("self", "{{self}}"),
            Var("user", "{{a}}"),
            Var("fine", "ok"),
        ]);

        Assert.Equal(["a", "b", "c", "self"], resolved.Cycle);
        Assert.Equal(["fine"], resolved.Values.Keys);
    }

    [Fact]
    public void Resolve_FirstOfRepeatedNamesWins()
    {
        var resolved = VariableSet.Resolve([Var("a", "1"), Var("a", "2")]);

        Assert.Equal("1", resolved.Values["a"]);
    }

    [Fact]
    public void Validate_AcceptsAValidEnvironment()
    {
        var environment = new EnvironmentDefinition("Development", [
            Var("baseUrl", "http://{{host}}"),
            Var("host", "localhost"),
            new EnvironmentVariable("token", "", Secret: true),
        ]);

        Assert.Empty(environment.Validate());
    }

    [Theory]
    [InlineData("", "a", "The environment needs a name.")]
    [InlineData("Dev", " ", "Every variable needs a name.")]
    [InlineData("Dev", "base url", "'base url' is not a valid variable name")]
    [InlineData("Dev", "a}", "'a}' is not a valid variable name")]
    public void Validate_RejectsBadNames(string name, string variable, string expected)
    {
        var errors = new EnvironmentDefinition(name, [Var(variable, "x")]).Validate();

        Assert.Contains(errors, e => e.StartsWith(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RejectsDuplicatesAndLoops()
    {
        var environment = new EnvironmentDefinition("Dev", [Var("a", "{{a}}"), Var("b", "1"), Var("b", "2")]);

        Assert.Equal(
            ["The variable 'b' is defined more than once.", "The variable 'a' refers to itself."],
            environment.Validate());
        Assert.Equal(["The variable 'b' is defined more than once."], environment.Validate(checkReferences: false));
    }

    [Fact]
    public void References_ListsReferencedNames()
    {
        Assert.Equal(["a", "b", "a"], VariableTemplate.References("{{a}}-{{ b }}-{{a}}-{x}"));
        Assert.Empty(VariableTemplate.References(""));
    }

    [Theory]
    [InlineData("staging", 1, "staging.yaml")]
    [InlineData("Local Dev", 2, "local-dev-2.yaml")]
    [InlineData("!!!", 1, "environment.yaml")]
    public void EnvironmentFileName_IsTheSlug(string name, int attempt, string expected)
    {
        Assert.Equal(expected, WorkspaceLayout.EnvironmentFileName(name, attempt));
    }
}
