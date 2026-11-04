namespace Missive.Domain.Requests;

/// <summary>How a request authenticates. Values may contain <c>{{variable}}</c> references.</summary>
public abstract record RequestAuth
{
    public static RequestAuth None { get; } = new NoAuth();
}

public sealed record NoAuth : RequestAuth;

public sealed record BasicAuth(string Username, string Password) : RequestAuth;

public sealed record BearerAuth(string Token) : RequestAuth;

public enum ApiKeyLocation
{
    Header,
    Query,
}

public sealed record ApiKeyAuth(string Key, string Value, ApiKeyLocation In = ApiKeyLocation.Header) : RequestAuth;
