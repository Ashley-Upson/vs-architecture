using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class ProjectRegionEligibility
{
    public static IReadOnlyList<string> Explain(DiagramModel diagram)
    {
        var reasons = new List<string>();
        if (diagram.Projects.Count != 1) reasons.Add("RequiresExactlyOneProject");
        var nodeIds = new HashSet<string>(
            diagram.Projects.SelectMany(project => project.Types).Select(node => node.Id)
                .Concat(diagram.ExternalDependencies.Select(node => node.Id)),
            StringComparer.Ordinal);
        foreach (var edge in diagram.Edges.OrderBy(edge => edge.Id, StringComparer.Ordinal))
        {
            if (!nodeIds.Contains(edge.SourceId)) reasons.Add($"MissingSource:{edge.Id}:{edge.SourceId}");
            if (!nodeIds.Contains(edge.TargetId)) reasons.Add($"MissingTarget:{edge.Id}:{edge.TargetId}");
            if (string.Equals(edge.SourceId, edge.TargetId, StringComparison.Ordinal)) reasons.Add($"UnsupportedSelfLoop:{edge.Id}");
        }
        return reasons.Distinct(StringComparer.Ordinal).OrderBy(reason => reason, StringComparer.Ordinal).ToArray();
    }
}
