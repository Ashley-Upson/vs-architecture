using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV6;

internal sealed class ArchitectureV6PreRoutingSpanSizer
{
    private const int EstimatedCharacterWidth = 8;
    private const int LabelPadding = 16;

    private readonly ArchitecturePlanningRequest request;
    private readonly ArchitectureProjectionResult projection;

    public ArchitectureV6PreRoutingSpanSizer(ArchitecturePlanningRequest request, ArchitectureProjectionResult projection)
    {
        this.request = request ?? throw new ArgumentNullException(nameof(request));
        this.projection = projection ?? throw new ArgumentNullException(nameof(projection));
    }

    public IReadOnlyList<ArchitectureV6NodeSpanRequirement> Build()
    {
        return projection.PhysicalNodes
            .Select(BuildRequirement)
            .ToArray();
    }

    private ArchitectureV6NodeSpanRequirement BuildRequirement(PlannedPhysicalNode node)
    {
        var label = string.IsNullOrWhiteSpace(node.DisplayLabel) ? node.SemanticName : node.DisplayLabel;
        var labelWidth = Math.Max(0, label?.Split('\n').Max(line => line.Length) ?? 0) * EstimatedCharacterWidth + LabelPadding;
        var top = ArchitectureV6TerminalCapacity.RequiredWidth(request,
            projection.PhysicalLinks.Count(link => link.DestinationPhysicalNodeId == node.PhysicalNodeId));
        var bottom = ArchitectureV6TerminalCapacity.RequiredWidth(request,
            projection.PhysicalLinks.Count(link => link.SourcePhysicalNodeId == node.PhysicalNodeId));
        var configuredMinimum = Math.Max(0, request.NodePlacement.MinimumNodeWidth);
        var requiredWidth = Math.Max(configuredMinimum, Math.Max(labelWidth, Math.Max(top, bottom)));
        var baseCellWidth = Math.Max(1, request.GridSizing.ConfiguredBaseCellWidth > 0
            ? request.GridSizing.ConfiguredBaseCellWidth
            : request.GridSizing.CellWidth);
        var span = Math.Max(3, (int)Math.Ceiling(requiredWidth / (double)baseCellWidth));
        if (span % 2 == 0) span++;
        return new ArchitectureV6NodeSpanRequirement(node.PhysicalNodeId, span, requiredWidth, labelWidth, top, bottom,
            configuredMinimum, $"pre-routing:max(label={labelWidth};top={top};bottom={bottom};minimum={configuredMinimum});baseCellWidth={baseCellWidth}");
    }
}
