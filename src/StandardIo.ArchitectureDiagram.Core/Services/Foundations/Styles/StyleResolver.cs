using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Models;

public sealed class StyleResolver
{
    private readonly DiagramSettings _settings;

    public StyleResolver(DiagramSettings settings)
    {
        _settings = settings;
    }

    public bool IsExcluded(string name, string fullName)
    {
        var ns = GetNamespace(fullName);
        return _settings.ExcludedNames.Any(pattern => GlobMatcher.IsMatch(name, pattern))
            || _settings.ExcludedNames.Any(pattern => GlobMatcher.IsMatch(fullName, pattern))
            || _settings.ExcludedNamespaces.Any(pattern => GlobMatcher.IsMatch(ns, pattern));
    }

    public NodeStyle Resolve(TypeNode node)
    {
        var exact = _settings.Overrides.FirstOrDefault(o => o.FullName == node.FullName);
        if (exact is not null)
        {
            return exact.Style;
        }

        var rule = _settings.StyleRules.FirstOrDefault(r =>
            GlobMatcher.IsMatch(node.Name, r.Match) || GlobMatcher.IsMatch(node.FullName, r.Match));

        return rule?.Style ?? new NodeStyle();
    }

    private static string GetNamespace(string fullName)
    {
        var index = fullName.LastIndexOf('.');
        return index <= 0 ? string.Empty : fullName.Substring(0, index);
    }
}
