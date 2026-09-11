using System;
using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV6;

internal static class ArchitectureV6ProjectionStage
{
    public static ArchitectureProjectionResult Build(ArchitecturePlanningRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        return new ArchitectureDiagramV6Planner.ProjectionBuilder(request).Build();
    }
}
