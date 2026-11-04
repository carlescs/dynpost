namespace Missive.Application.Common;

/// <summary>The result value of an operation that has nothing to return.</summary>
public readonly record struct Unit
{
    public static Unit Default => default;
}
