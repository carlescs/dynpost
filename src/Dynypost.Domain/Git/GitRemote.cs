namespace Dynypost.Domain.Git;

/// <summary>A remote repository of the workspace's repository.</summary>
/// <param name="Name">The remote's name, for example <c>origin</c>.</param>
/// <param name="FetchUrl">The URL pulled from.</param>
/// <param name="PushUrl">The URL pushed to; the same as <paramref name="FetchUrl"/> unless a push URL is set.</param>
public sealed record GitRemote(string Name, string FetchUrl, string PushUrl);
