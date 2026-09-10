// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
internal sealed class BranchSpacingLayoutRuleProcessingService : ILayoutRuleProcessingService
{
    public void ApplyRule(RenderModel renderModel)
    {
        foreach (var project in renderModel.Projects)
        foreach (var group in LayoutGraph.BranchGroups(project).ToArray())
        {
            var branches = group.OrderBy(root => root.X).ThenBy(root => root.Id, StringComparer.Ordinal).Select(root =>
            {
                var nodes = LayoutGraph.OwnedBranch(project, root.Id);
                return (Root: root, Left: nodes.Min(node => node.X), Right: nodes.Max(node => node.X + node.Width));
            }).ToArray();
            var distances = new double[branches.Length];
            var blocks = new List<(int Start, int Count, double Sum)>();
            for (int index = 0; index < branches.Length; index++)
            {
                if (index > 0) distances[index] = distances[index - 1] + branches[index - 1].Right - branches[index - 1].Left + (renderModel.IsProjectGraph ? renderModel.Configuration.Architecture.ProjectSpacing : renderModel.Configuration.Architecture.NodeSpacing);
                blocks.Add((index, 1, branches[index].Left - distances[index]));
                while (blocks.Count > 1 && blocks[^2].Sum / blocks[^2].Count > blocks[^1].Sum / blocks[^1].Count)
                {
                    var last = blocks[^1]; var previous = blocks[^2];
                    blocks.RemoveAt(blocks.Count - 1);
                    blocks[^1] = (previous.Start, previous.Count + last.Count, previous.Sum + last.Sum);
                }
            }
            foreach (var block in blocks)
            for (int offset = 0; offset < block.Count; offset++)
            {
                int index = block.Start + offset;
                LayoutGraph.MoveSubtree(project, branches[index].Root.Id, block.Sum / block.Count + distances[index] - branches[index].Left);
            }
        }
    }
}
