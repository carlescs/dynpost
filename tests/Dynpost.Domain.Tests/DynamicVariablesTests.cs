using Dynpost.Domain.Requests;

namespace Dynpost.Domain.Tests;

public class DynamicVariablesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 9, 30, 15, 250, TimeSpan.Zero);

    [Fact]
    public void Generate_Timestamps_UseTheClock()
    {
        Assert.Equal(Now.ToUnixTimeSeconds().ToString(), DynamicVariables.Generate("$timestamp", Now, new Random(1)));
        Assert.Equal("2026-11-15T09:30:15.250Z", DynamicVariables.Generate("$isoTimestamp", Now, new Random(1)));
    }

    [Fact]
    public void Generate_Uuid_IsAVersion4Uuid()
    {
        var value = DynamicVariables.Generate("$uuid", Now, new Random(1));

        Assert.Matches("^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$", value);
    }

    [Fact]
    public void Generate_RandomInt_StaysInItsRange()
    {
        var random = new Random(3);
        for (var i = 0; i < 200; i++)
        {
            var value = int.Parse(DynamicVariables.Generate("$randomInt 5 7", Now, random)!);
            Assert.InRange(value, 5, 7);
            Assert.InRange(int.Parse(DynamicVariables.Generate("$randomInt", Now, random)!), 0, 1000);
        }

        Assert.Equal("-2", DynamicVariables.Generate("$randomInt -2 -2", Now, random));
    }

    [Theory]
    [InlineData("$nope")]
    [InlineData("$randomInt 9 1")]
    [InlineData("$randomInt a b")]
    [InlineData("$randomInt 1")]
    [InlineData("$uuid v4")]
    public void Generate_RejectsUnknownNamesAndBadArguments(string reference) =>
        Assert.Null(DynamicVariables.Generate(reference, Now, new Random(1)));

    [Fact]
    public void With_GeneratesOnceAndKeepsTheValueForTheSend()
    {
        var variables = DynamicVariables.With(new Dictionary<string, string> { ["id"] = "1" }, Now, new Random(5));
        var missing = new SortedSet<string>();

        var text = VariableTemplate.Resolve("{{$uuid}}/{{ $uuid }}/{{id}}/{{$randomInt  1   1}}/{{$nope}}", variables, missing);

        var parts = text.Split('/');
        Assert.Equal(parts[0], parts[1]);
        Assert.Equal("1", parts[2]);
        Assert.Equal("1", parts[3]);
        Assert.Equal("{{$nope}}", parts[4]);
        Assert.Equal(["$nope"], missing);
    }

    [Fact]
    public void With_DefinedVariablesWinOverBuiltInOnes()
    {
        var variables = DynamicVariables.With(new Dictionary<string, string> { ["$uuid"] = "fixed" }, Now, new Random(5));

        Assert.Equal("fixed", VariableTemplate.Resolve("{{$uuid}}", variables, new SortedSet<string>()));
    }

    [Fact]
    public void References_AllowArgumentsOnlyForBuiltInVariables()
    {
        Assert.Equal(["$randomInt 1 100"], VariableTemplate.References("{{ $randomInt 1 100 }}"));
        Assert.Empty(VariableTemplate.References("{{a b}}"));
    }
}
