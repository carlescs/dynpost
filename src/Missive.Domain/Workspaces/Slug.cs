using System.Globalization;
using System.Text;

namespace Missive.Domain.Workspaces;

/// <summary>Turns request names into file-name slugs, e.g. "Create user" → <c>create-user</c>.</summary>
public static class Slug
{
    public const string Fallback = "request";

    public static string From(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Fallback;
        }

        var builder = new StringBuilder(name.Length);
        var pendingDash = false;

        foreach (var c in name.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsAsciiLetterOrDigit(c))
            {
                if (pendingDash && builder.Length > 0)
                {
                    builder.Append('-');
                }

                builder.Append(char.ToLowerInvariant(c));
                pendingDash = false;
            }
            else
            {
                pendingDash = true;
            }
        }

        return builder.Length == 0 ? Fallback : builder.ToString();
    }
}
