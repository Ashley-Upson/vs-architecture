using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class ProjectLabelGeometryMeasurer
{
    public static IReadOnlyDictionary<string, ProjectLabelGeometry> Measure(
        IReadOnlyDictionary<string, ProjectLayout> projects,
        int headerHeight,
        int clearance) => projects.Values.OrderBy(item => item.Project.Order)
        .ToDictionary(item => item.Project.Id, item =>
        {
            var width = Math.Min(item.Rect.Width,
                LinkConnectionDemandCalculator.EstimatedTextWidth(item.Project.Name, item.Project.Name));
            var text = new Rect(item.Rect.CenterX - width / 2, item.Rect.Y, width, headerHeight);
            return new ProjectLabelGeometry(
                item.Project.Id, item.Rect, text, text.Inflate(Math.Max(0, clearance)));
        }, StringComparer.Ordinal);
}
