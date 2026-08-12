using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV7;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV7;

public sealed class ArchitectureV7PreRoutingNodeSpanSizer
{
    public ArchitectureV7NodeSpanSizingResult Size(
        ArchitectureV7PositionalOwnershipResult ownership,
        ArchitectureV7PrePlacementConfiguration configuration)
    {
        if (ownership is null) throw new ArgumentNullException(nameof(ownership));
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));
        if (configuration.ConfiguredBaseCellWidth <= 0) throw new ArgumentOutOfRangeException(nameof(configuration), "Configured base cell width must be positive.");
        if (configuration.LabelCharacterWidth <= 0) throw new ArgumentOutOfRangeException(nameof(configuration), "Configured label character width must be positive.");
        if (configuration.TerminalPortSpacing <= 0) throw new ArgumentOutOfRangeException(nameof(configuration), "Configured terminal port spacing must be positive.");
        if (configuration.TerminalInset < 0) throw new ArgumentOutOfRangeException(nameof(configuration), "Configured terminal inset cannot be negative.");

        var incoming = ownership.Projection.PhysicalLinks.GroupBy(link => link.DestinationPhysicalNodeId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var outgoing = ownership.Projection.PhysicalLinks.GroupBy(link => link.SourcePhysicalNodeId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var requirements = ownership.Projection.PhysicalNodes.Select(node =>
        {
            var visibleLabel = node.IsExternal ? "[External]\n" + node.Name : node.Name;
            var labelRequirement = visibleLabel.Split('\n').Max(line => line.Length) * configuration.LabelCharacterWidth + configuration.LabelHorizontalMargin;
            var incomingCount = incoming.TryGetValue(node.PhysicalNodeId, out var topCount) ? topCount : 0;
            var outgoingCount = outgoing.TryGetValue(node.PhysicalNodeId, out var bottomCount) ? bottomCount : 0;
            var incomingRequirement = TerminalWidth(incomingCount, configuration);
            var outgoingRequirement = TerminalWidth(outgoingCount, configuration);
            var requiredWidth = Math.Max(configuration.ConfiguredMinimumWidth,
                Math.Max(labelRequirement, Math.Max(incomingRequirement, outgoingRequirement)));
            var minimumLegalSpan = MinimumOddSpan(requiredWidth, configuration.ConfiguredBaseCellWidth);
            var span = minimumLegalSpan;
            return new ArchitectureV7NodeSpanRequirement(node.PhysicalNodeId, span, labelRequirement,
                incomingRequirement, outgoingRequirement, configuration.ConfiguredMinimumWidth, requiredWidth,
                configuration.ConfiguredBaseCellWidth,
                $"v7-pre-routing;label={labelRequirement};incoming={incomingRequirement};outgoing={outgoingRequirement};minimum={configuration.ConfiguredMinimumWidth};base-cell={configuration.ConfiguredBaseCellWidth};minimum-legal-span={minimumLegalSpan}",
                incomingCount, outgoingCount, minimumLegalSpan, Math.Max(incomingRequirement, outgoingRequirement), span * configuration.ConfiguredBaseCellWidth);
        }).OrderBy(item => item.PhysicalNodeId, StringComparer.Ordinal).ToArray();

        return new ArchitectureV7NodeSpanSizingResult(ownership, requirements, Fingerprint(ownership.FreezeFingerprint, requirements));
    }

    private static int TerminalWidth(int count, ArchitectureV7PrePlacementConfiguration configuration) =>
        count <= 0 ? 0 : checked(configuration.TerminalInset * 2 + (count - 1) * configuration.TerminalPortSpacing);

    private static int MinimumOddSpan(int requiredWidth, int baseCellWidth)
    {
        var span = Math.Max(3, (int)Math.Ceiling(requiredWidth / (double)baseCellWidth));
        return span % 2 == 0 ? checked(span + 1) : span;
    }

    private static string Fingerprint(string ownershipFingerprint, IReadOnlyList<ArchitectureV7NodeSpanRequirement> requirements)
    {
        var text = ownershipFingerprint + "#" + string.Join("|", requirements.Select(item => string.Join(":", item.PhysicalNodeId,
            item.LogicalSpan, item.VisibleLabelRequirement, item.IncomingTerminalRequirement, item.OutgoingTerminalRequirement,
            item.RequiredWidth, item.ConfiguredBaseCellWidth)));
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", string.Empty);
    }
}
