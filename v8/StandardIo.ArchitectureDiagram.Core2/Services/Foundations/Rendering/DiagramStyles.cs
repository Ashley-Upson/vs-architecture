// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
namespace StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
internal static class DiagramStyles
{
    internal static string GetRoleColour(string name, bool isInternal = true, string[]? layerColours = null)
    {
        layerColours ??= new StandardIo.ArchitectureDiagram.Core2.Models.ArchitectureRenderConfiguration().LayerColours;
        return layerColours[(int)GetRoleCategory(name, isInternal)];
    }

    internal static StandardIo.ArchitectureDiagram.Core2.Models.RenderNodeCategory GetRoleCategory(string name, bool isInternal = true)
    {
        if (!isInternal) return StandardIo.ArchitectureDiagram.Core2.Models.RenderNodeCategory.Exposure;
        if (name.Contains(value: ".Orchestrations.") || name.EndsWith(value: "OrchestrationService"))
        {
            return StandardIo.ArchitectureDiagram.Core2.Models.RenderNodeCategory.Orchestration;
        }

        if (name.Contains(value: ".Processings.") || name.EndsWith(value: "ProcessingService"))
        {
            return StandardIo.ArchitectureDiagram.Core2.Models.RenderNodeCategory.Processing;
        }

        if (name.Contains(value: ".Foundations."))
        {
            return StandardIo.ArchitectureDiagram.Core2.Models.RenderNodeCategory.Foundation;
        }

        if (name.Contains(value: ".Brokers.") || name.EndsWith(value: "Broker"))
        {
            return StandardIo.ArchitectureDiagram.Core2.Models.RenderNodeCategory.Broker;
        }

        if (name.Contains(value: ".Exposures.") || name.EndsWith(value: "Controller") || name.EndsWith(value: "Manager") || name.EndsWith(value: "Hub") || name.EndsWith(value: "Context") || name.EndsWith(value: "Client"))
        {
            return StandardIo.ArchitectureDiagram.Core2.Models.RenderNodeCategory.Exposure;
        }

        return StandardIo.ArchitectureDiagram.Core2.Models.RenderNodeCategory.Other;
    }
}