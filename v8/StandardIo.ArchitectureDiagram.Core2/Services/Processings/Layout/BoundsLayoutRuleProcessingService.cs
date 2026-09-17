// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
internal sealed class BoundsLayoutRuleProcessingService : ArchitectureLayoutRuleProcessingService
{
    protected override System.Collections.Generic.IEnumerable<string> GetArchitectureViolations(RenderModel model) => LayoutConditions.Bounds(model);
    protected override void ApplyArchitectureRule(RenderModel renderModel)
    {
        double left = 40;
        for (int projectIndex = 0; projectIndex < renderModel.Projects.Length; projectIndex++)
        {
            var project = renderModel.Projects[projectIndex];
            double shift = 40 - project.Nodes.Select(node => node.X).DefaultIfEmpty(40).Min();
            for (int index = 0; index < project.Nodes.Length; index++)
            {
                var node = project.Nodes[index];
                double x = node.X + shift;
                project.Nodes[index] = node with { X = x };
            }
            double width = Math.Max(300, project.Nodes.Select(node => node.X + node.Width + 40).DefaultIfEmpty(300).Max());
            double height = project.Nodes.Select(node => node.Y + node.Height + 40).DefaultIfEmpty(120).Max();
            renderModel.Projects[projectIndex] = project with { X = renderModel.CrossProjectConnections.Length == 0 ? left : project.X, Y = renderModel.CrossProjectConnections.Length == 0 ? 40 : project.Y, Width = width, Height = height };
            left += width + renderModel.Configuration.Architecture.ProjectSpacing;
        }
    }
}
