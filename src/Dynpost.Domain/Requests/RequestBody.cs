namespace Dynpost.Domain.Requests;

public enum BodyType
{
    None,
    Json,
    Text,
    Form,
    UrlEncoded,
    Graphql,
    Binary,
}

/// <summary>The request body. <see cref="BodyType.None"/> means the request has no body.</summary>
/// <remarks>
/// <c>form</c> and <c>urlencoded</c> bodies keep their fields in <see cref="Fields"/>. Older files
/// wrote them as text in <see cref="Content"/> (one <c>name=value</c> per line); those keep their
/// text, and <see cref="Fields"/> stays <c>null</c>, until the body is edited, so a file is only
/// rewritten in the new shape when its body changes. <see cref="FormFields.Of"/> reads either.
/// </remarks>
public sealed record RequestBody(BodyType Type, string Content, string Variables = "")
{
    public static RequestBody None { get; } = new(BodyType.None, string.Empty);

    /// <summary>The fields of a <c>form</c> or <c>urlencoded</c> body; <c>null</c> when the body is kept as text.</summary>
    public IReadOnlyList<FormField>? Fields { get; init; }

    /// <summary>A <c>form</c> or <c>urlencoded</c> body made of <paramref name="fields"/>.</summary>
    public static RequestBody WithFields(BodyType type, IEnumerable<FormField> fields) =>
        new(type, string.Empty) { Fields = [.. fields] };

    public bool Equals(RequestBody? other) =>
        other is not null
        && Type == other.Type
        && Content == other.Content
        && Variables == other.Variables
        && (Fields is null ? other.Fields is null : other.Fields is not null && Fields.SequenceEqual(other.Fields));

    public override int GetHashCode() => HashCode.Combine(Type, Content, Variables, Fields?.Count);

    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append($"Type = {Type}, Content = {Content}, Variables = {Variables}");
        if (Fields is not null)
        {
            builder.Append($", Fields = [{string.Join(", ", Fields)}]");
        }

        return true;
    }
}
