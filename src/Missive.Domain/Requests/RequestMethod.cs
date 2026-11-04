namespace Missive.Domain.Requests;

/// <summary>The HTTP methods a request definition can use.</summary>
public enum RequestMethod
{
    Get,
    Post,
    Put,
    Patch,
    Delete,
    Head,
    Options,
}

public static class RequestMethods
{
    /// <summary>The canonical (upper-case) name of a method, e.g. <c>GET</c>.</summary>
    public static string ToName(this RequestMethod method) => method.ToString().ToUpperInvariant();

    /// <summary>Parses a method name case-insensitively; returns <c>false</c> for unknown names.</summary>
    public static bool TryParse(string? name, out RequestMethod method)
    {
        method = RequestMethod.Get;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        foreach (var candidate in Enum.GetValues<RequestMethod>())
        {
            if (string.Equals(candidate.ToName(), name.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                method = candidate;
                return true;
            }
        }

        return false;
    }
}
