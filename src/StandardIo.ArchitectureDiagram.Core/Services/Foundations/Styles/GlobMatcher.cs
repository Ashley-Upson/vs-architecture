using System.Text.RegularExpressions;

namespace StandardIo.ArchitectureDiagram.Core.Models;

public static class GlobMatcher
{
    public static bool IsMatch(string value, string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return false;
        }

        var regex = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        return Regex.IsMatch(value ?? string.Empty, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
