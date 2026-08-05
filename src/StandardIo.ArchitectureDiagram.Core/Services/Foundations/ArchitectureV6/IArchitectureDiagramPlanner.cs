using StandardIo.ArchitectureDiagram.Core.Models.ArchitectureV6;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.ArchitectureV6;

public interface IArchitectureDiagramPlanner
{
    PlannedArchitectureDiagram Plan(ArchitecturePlanningRequest request);
}
