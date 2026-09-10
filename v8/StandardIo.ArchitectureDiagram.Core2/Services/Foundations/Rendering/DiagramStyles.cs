namespace StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
internal static class DiagramStyles
{
    internal static string GetRoleColour(string name)
    {
        if (name.Contains(".Orchestrations.") || name.EndsWith("OrchestrationService")) return "#00506b";
        if (name.Contains(".Processings.") || name.EndsWith("ProcessingService")) return "#5100aa";
        if (name.Contains(".Foundations.")) return "#005000";
        if (name.Contains(".Brokers.") || name.EndsWith("Broker")) return "#335600";
        if (name.Contains(".Exposures.") || name.EndsWith("Controller") || name.EndsWith("Manager")) return "#003b99";
        return "#374151";
    }

}
