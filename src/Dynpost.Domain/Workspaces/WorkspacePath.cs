namespace Dynpost.Domain.Workspaces;

/// <summary>
/// A path relative to the workspace root, always with <c>/</c> separators. It can never point
/// outside the workspace: rooted paths and <c>..</c> segments are rejected. The empty path is the
/// workspace root itself.
/// </summary>
public sealed record WorkspacePath
{
    private WorkspacePath(string value)
    {
        Value = value;
    }

    public static WorkspacePath Root { get; } = new(string.Empty);

    public string Value { get; }

    public bool IsRoot => Value.Length == 0;

    /// <summary>The last segment, e.g. <c>get-user.yaml</c>.</summary>
    public string FileName => IsRoot ? string.Empty : Value[(Value.LastIndexOf('/') + 1)..];

    /// <summary>The containing folder, or <see cref="Root"/> for top-level entries.</summary>
    public WorkspacePath Parent
    {
        get
        {
            var index = Value.LastIndexOf('/');
            return index < 0 ? Root : new WorkspacePath(Value[..index]);
        }
    }

    public IReadOnlyList<string> Segments => IsRoot ? [] : Value.Split('/');

    public static bool TryParse(string? value, out WorkspacePath path)
    {
        path = Root;
        if (value is null)
        {
            return false;
        }

        var normalized = value.Replace('\\', '/').Trim('/');
        if (normalized.Length == 0)
        {
            return true;
        }

        // Rooted ("C:", "/x" was trimmed above but "//server" or drive letters are not relative).
        if (normalized.Contains(':') || value.StartsWith('/') || value.StartsWith('\\'))
        {
            return false;
        }

        foreach (var segment in normalized.Split('/'))
        {
            if (segment.Length == 0 || segment == "." || segment == ".." || segment.Any(char.IsControl))
            {
                return false;
            }
        }

        path = new WorkspacePath(normalized);
        return true;
    }

    public static WorkspacePath Parse(string value) =>
        TryParse(value, out var path) ? path : throw new ArgumentException($"'{value}' is not a valid workspace path.", nameof(value));

    public WorkspacePath Combine(string segment)
    {
        if (!TryParse(IsRoot ? segment : $"{Value}/{segment}", out var path) || segment.Contains('/') || segment.Contains('\\'))
        {
            throw new ArgumentException($"'{segment}' is not a valid path segment.", nameof(segment));
        }

        return path;
    }

    public override string ToString() => Value;
}
