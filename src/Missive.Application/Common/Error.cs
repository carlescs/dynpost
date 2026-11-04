namespace Missive.Application.Common;

/// <summary>An expected failure: a stable machine-readable code and a message for the user.</summary>
public sealed record Error(string Code, string Message);
