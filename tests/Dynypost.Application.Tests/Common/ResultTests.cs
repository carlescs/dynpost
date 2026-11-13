using Dynypost.Application.Common;

namespace Dynypost.Application.Tests.Common;

public class ResultTests
{
    [Fact]
    public void Success_HasAValueAndNoError()
    {
        Result<int> result = 42;

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Failure_HasAnErrorAndThrowsOnValue()
    {
        Result<int> result = new Error("test.failed", "It failed.");

        Assert.True(result.IsFailure);
        Assert.Equal("test.failed", result.Error?.Code);
        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void Map_TransformsSuccessAndPassesFailureThrough()
    {
        Result<int> success = 21;
        Result<int> failure = new Error("test.failed", "It failed.");

        Assert.Equal("42", success.Map(v => (v * 2).ToString()).Value);
        Assert.Equal("test.failed", failure.Map(v => v.ToString()).Error?.Code);
    }
}
