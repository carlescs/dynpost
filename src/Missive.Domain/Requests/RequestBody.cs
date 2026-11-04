namespace Missive.Domain.Requests;

public enum BodyType
{
    None,
    Json,
    Text,
    Form,
    UrlEncoded,
}

/// <summary>The request body. <see cref="BodyType.None"/> means the request has no body.</summary>
public sealed record RequestBody(BodyType Type, string Content)
{
    public static RequestBody None { get; } = new(BodyType.None, string.Empty);
}
