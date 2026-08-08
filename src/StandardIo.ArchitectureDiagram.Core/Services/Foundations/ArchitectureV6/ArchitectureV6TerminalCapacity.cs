using System;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV6;

internal static class ArchitectureV6TerminalCapacity
{
    public static int Inset(ArchitecturePlanningRequest request) =>
        Math.Max(request.RoutePlanning.MinimumPortSpacing, request.GridSizing.NodeToRouteClearance);

    public static int RequiredWidth(ArchitecturePlanningRequest request, int terminalCount)
    {
        if (terminalCount <= 0) return 0;
        var spacing = Math.Max(1, request.RoutePlanning.MinimumPortSpacing);
        var inset = Inset(request);
        return checked(inset * 2 + Math.Max(0, terminalCount - 1) * spacing);
    }

    public static int RequiredOddSpan(ArchitecturePlanningRequest request, int terminalCount)
    {
        var width = RequiredWidth(request, terminalCount);
        var cellWidth = Math.Max(1, request.GridSizing.CellWidth);
        var span = Math.Max(3, (int)Math.Ceiling((double)width / cellWidth));
        return span % 2 == 0 ? span + 1 : span;
    }

    public static bool IsInsideInset(ArchitecturePlanningRequest request, double x, double left, double width)
    {
        var inset = Math.Min(Math.Max(1, width / 2 - 1), Inset(request));
        return x >= left + inset && x <= left + width - inset && x > left && x < left + width;
    }
}
