using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal enum TerminalAttachmentSide { IncomingTop, OutgoingBottom }

internal sealed record TerminalNodeDemand(
    string NodeId,
    int CurrentWidth,
    int TextSpaceRequirement,
    int IncomingAttachmentSpaceRequirement,
    int OutgoingAttachmentSpaceRequirement,
    int RequiredWidth,
    int TotalHorizontalPadding,
    int AttachmentSeparation);

internal sealed record TerminalAttachmentRequest(
    string RouteId,
    int RemoteAxisCoordinate,
    TerminalAttachmentSide Side);

internal sealed record TerminalAttachment(
    string RouteId,
    TerminalAttachmentSide Side,
    int AxisCoordinate,
    int Order);

internal static class TerminalDemandCalculator
{
    internal const int EstimatedCharacterWidth = 8;

    public static int AttachmentSeparation(int edgePortSpacing, int parallelLaneSpacing) =>
        Math.Max(edgePortSpacing, checked(parallelLaneSpacing * 2));

    public static TerminalNodeDemand Measure(
        string nodeId,
        int currentWidth,
        int measuredTextWidth,
        int incomingCount,
        int outgoingCount,
        int attachmentSeparation,
        int totalHorizontalPadding)
    {
        if (currentWidth < 0 || measuredTextWidth < 0 || incomingCount < 0 || outgoingCount < 0 ||
            attachmentSeparation < 0 || totalHorizontalPadding < 0)
            throw new ArgumentOutOfRangeException(nameof(currentWidth), "Terminal measurements cannot be negative.");

        var text = checked(measuredTextWidth + totalHorizontalPadding);
        var incoming = checked(Math.Max(0, incomingCount - 1) * attachmentSeparation + totalHorizontalPadding);
        var outgoing = checked(Math.Max(0, outgoingCount - 1) * attachmentSeparation + totalHorizontalPadding);
        var required = Math.Max(currentWidth, Math.Max(text, Math.Max(incoming, outgoing)));
        return new TerminalNodeDemand(
            nodeId, currentWidth, text, incoming, outgoing, required,
            totalHorizontalPadding, attachmentSeparation);
    }

    public static IReadOnlyList<TerminalAttachment> Allocate(
        Rect node,
        IEnumerable<TerminalAttachmentRequest> requests,
        int totalHorizontalPadding,
        int attachmentSeparation)
    {
        var result = new List<TerminalAttachment>();
        foreach (var side in requests.GroupBy(request => request.Side).OrderBy(group => group.Key))
        {
            var ordered = side.OrderBy(request => request.RemoteAxisCoordinate)
                .ThenBy(request => request.RouteId, StringComparer.Ordinal).ToArray();
            var requiredSpan = Math.Max(0, ordered.Length - 1) * attachmentSeparation;
            var usableWidth = node.Width - totalHorizontalPadding;
            if (requiredSpan > usableWidth)
                throw new InvalidOperationException(
                    $"Node width {node.Width} cannot fit {ordered.Length} {side.Key} attachments " +
                    $"at {attachmentSeparation}px separation with {totalHorizontalPadding}px total padding.");
            var start = node.CenterX - requiredSpan / 2.0;
            for (var index = 0; index < ordered.Length; index++)
            {
                var coordinate = (int)Math.Round(start + index * attachmentSeparation);
                result.Add(new TerminalAttachment(ordered[index].RouteId, side.Key, coordinate, index));
            }
        }
        return result.OrderBy(item => item.Side).ThenBy(item => item.Order)
            .ThenBy(item => item.RouteId, StringComparer.Ordinal).ToArray();
    }

    public static int EstimatedTextWidth(string name, string fullName) =>
        Math.Max(name?.Length ?? 0, (fullName?.Length ?? 0) / 2) * EstimatedCharacterWidth;
}
