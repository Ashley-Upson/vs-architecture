using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV6;

internal static class ArchitectureV6RoleResolver
{
    public static string Resolve(string value, IReadOnlyList<ArchitectureV6RoleRule>? rules)
    {
        if (rules is null) return "Unmatched";
        return rules
            .OrderBy(rule => rule.Order)
            .ThenBy(rule => rule.Name, StringComparer.Ordinal)
            .FirstOrDefault(rule => ToRegex(rule.Pattern).IsMatch(value ?? string.Empty))?.Name ?? "Unmatched";
    }

    public static IReadOnlyDictionary<string, int> CountMatches(
        IEnumerable<string> values,
        IReadOnlyList<ArchitectureV6RoleRule>? rules)
    {
        var ordered = (rules ?? Array.Empty<ArchitectureV6RoleRule>())
            .OrderBy(rule => rule.Order)
            .ThenBy(rule => rule.Name, StringComparer.Ordinal)
            .ToArray();
        var counts = ordered.ToDictionary(rule => rule.Name, _ => 0, StringComparer.Ordinal);
        foreach (var value in values)
        {
            var role = Resolve(value, ordered);
            if (counts.ContainsKey(role)) counts[role]++;
        }
        return counts;
    }

    private static Regex ToRegex(string? pattern)
    {
        var value = string.IsNullOrWhiteSpace(pattern) ? ".*" : pattern!;
        if (value.Contains(".*", StringComparison.Ordinal) || value.Contains("$", StringComparison.Ordinal) || value.Contains("(", StringComparison.Ordinal))
            return new Regex(value, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return new Regex("^" + Regex.Escape(value).Replace("\\*", ".*") + "$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
