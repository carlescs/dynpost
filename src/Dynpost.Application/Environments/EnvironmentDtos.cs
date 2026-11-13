namespace Dynpost.Application.Environments;

/// <param name="Secret">Whether the value is kept on this computer only instead of in the file.</param>
public sealed record EnvironmentVariableDto(string Name, string Value, bool Secret = false);

/// <param name="CookieJar">Whether requests sent with the environment keep cookies between them.</param>
public sealed record EnvironmentDto(string Name, IReadOnlyList<EnvironmentVariableDto> Variables, bool CookieJar = false);

/// <summary>An environment as stored: its (possibly new) id and its content.</summary>
/// <param name="Id">The environment's file name in the <c>environments</c> folder.</param>
public sealed record SavedEnvironmentDto(string Id, EnvironmentDto Environment);

/// <param name="Name">The environment's name, or its file name when the file can't be read.</param>
/// <param name="Error">Why the file can't be read, if it can't.</param>
/// <param name="LocalOnly">Whether the file is kept on this computer only, never committed.</param>
public sealed record EnvironmentSummaryDto(string Id, string Name, string? Error = null, bool LocalOnly = false);

/// <param name="Active">The id of the selected environment, or <c>null</c> when none is.</param>
/// <param name="Environments">Every environment of the workspace, sorted by name.</param>
public sealed record EnvironmentListDto(string? Active, IReadOnlyList<EnvironmentSummaryDto> Environments);
