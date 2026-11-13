namespace Dynypost.Domain.Requests;

public enum FormFieldKind
{
    Text,
    File,
}

/// <summary>A field of a <c>form</c> (multipart) or <c>urlencoded</c> body.</summary>
/// <param name="Value">The text of a text field, or the path of a file field (workspace-relative or absolute).</param>
/// <param name="ContentType">The part's content type; empty to use the default (for files, from the extension).</param>
/// <param name="FileName">The file name sent for a file part; empty to use the file's own name.</param>
/// <param name="Encode">For <c>urlencoded</c> bodies: percent-encode the name and value. Otherwise they are sent as written.</param>
public sealed record FormField(
    string Name,
    string Value = "",
    FormFieldKind Kind = FormFieldKind.Text,
    bool Enabled = true,
    string ContentType = "",
    string FileName = "",
    bool Encode = false)
{
    public bool IsFile => Kind == FormFieldKind.File;

    public static FormField File(string name, string path, string contentType = "", string fileName = "", bool enabled = true) =>
        new(name, path, FormFieldKind.File, enabled, contentType, fileName);
}

/// <summary>Reads the fields of <c>form</c> and <c>urlencoded</c> bodies.</summary>
public static class FormFields
{
    /// <summary>
    /// The fields of a <c>form</c> or <c>urlencoded</c> body: its <see cref="RequestBody.Fields"/>,
    /// or those parsed from the text of an older file. Empty for other body types.
    /// </summary>
    public static IReadOnlyList<FormField> Of(RequestBody body) =>
        body.Type is BodyType.Form or BodyType.UrlEncoded
            ? body.Fields ?? ParseText(body.Content, body.Type)
            : [];

    /// <summary>
    /// Parses the text form older files used: one <c>name=value</c> per line. In <c>form</c> bodies
    /// <c>name=@path</c> is a file, optionally followed by <c>;type=</c> and a content type. In
    /// <c>urlencoded</c> bodies a line may hold several pairs joined with <c>&amp;</c>, and values
    /// are already encoded (sent as written).
    /// </summary>
    public static IReadOnlyList<FormField> ParseText(string content, BodyType type)
    {
        var lines = content.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0);
        if (type == BodyType.UrlEncoded)
        {
            lines = lines.SelectMany(line => line.Split('&', StringSplitOptions.RemoveEmptyEntries));
        }

        var fields = new List<FormField>();
        foreach (var line in lines)
        {
            var index = line.IndexOf('=');
            var name = index >= 0 ? line[..index].Trim() : line;
            var value = index >= 0 ? line[(index + 1)..].Trim() : string.Empty;
            if (type == BodyType.Form && value.StartsWith('@'))
            {
                var file = value[1..];
                var contentType = string.Empty;
                var typeIndex = file.LastIndexOf(";type=", StringComparison.OrdinalIgnoreCase);
                if (typeIndex >= 0)
                {
                    contentType = file[(typeIndex + 6)..].Trim();
                    file = file[..typeIndex].Trim();
                }

                fields.Add(FormField.File(name, file, contentType));
            }
            else
            {
                fields.Add(new FormField(name, value));
            }
        }

        return fields;
    }
}
