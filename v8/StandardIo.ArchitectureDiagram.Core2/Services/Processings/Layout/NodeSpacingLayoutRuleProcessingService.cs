// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
internal sealed class NodeSpacingLayoutRuleProcessingService : ILayoutRuleProcessingService
{
    public void ApplyRule(RenderModel renderModel)
    {
        foreach (var project in renderModel.Projects)
        foreach (double y in project.Nodes.Select(node => node.Y).Distinct().OrderBy(y => y))
        {
            var row = project.Nodes.Where(node => node.Y == y);
            var ordered = row.OrderBy(type => LayoutGraph.Centre(type)).ThenBy(type => type.Id, StringComparer.Ordinal).ToArray();
            var distances = new double[ordered.Length];
            for (int index = 1; index < ordered.Length; index++)
                distances[index] = distances[index - 1] + ordered[index - 1].Width / 2 + renderModel.Configuration.Architecture.NodeSpacing + ordered[index].Width / 2;
            var blocks = new List<(int Start, int Count, double Sum)>();
            for (int indexInRow = 0; indexInRow < ordered.Length; indexInRow++)
            {
                blocks.Add((indexInRow, 1, LayoutGraph.Centre(ordered[indexInRow]) - distances[indexInRow]));
                while (blocks.Count > 1 && blocks[^2].Sum / blocks[^2].Count > blocks[^1].Sum / blocks[^1].Count)
                {
                    var last = blocks[^1];
                    var previous = blocks[^2];
                    blocks.RemoveAt(blocks.Count - 1);
                    blocks[^1] = (previous.Start, previous.Count + last.Count, previous.Sum + last.Sum);
                }
            }

            foreach (var block in blocks)
            {
                for (int offset = 0; offset < block.Count; offset++)
                {
                    int indexInRow = block.Start + offset;
                    LayoutGraph.MoveSubtree(project, ordered[indexInRow].Id, block.Sum / block.Count + distances[indexInRow] - LayoutGraph.Centre(ordered[indexInRow]));
                }
            }
        }
    }
}
