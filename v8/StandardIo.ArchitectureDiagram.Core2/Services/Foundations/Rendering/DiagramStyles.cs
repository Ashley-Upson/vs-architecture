// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
namespace StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
internal static class DiagramStyles
{
    internal static string GetRoleColour(string name, bool isInternal = true, string[]? layerColours = null)
    {
        layerColours ??= new StandardIo.ArchitectureDiagram.Core2.Models.ArchitectureRenderConfiguration().LayerColours;
        if (name.Contains(value: ".Orchestrations.") || name.EndsWith(value: "OrchestrationService"))
        {
            return layerColours[0];
        }

        if (name.Contains(value: ".Processings.") || name.EndsWith(value: "ProcessingService"))
        {
            return layerColours[1];
        }

        if (name.Contains(value: ".Foundations."))
        {
            return layerColours[2];
        }

        if (name.Contains(value: ".Brokers.") || name.EndsWith(value: "Broker"))
        {
            return layerColours[3];
        }

        if (name.Contains(value: ".Exposures.") || name.EndsWith(value: "Controller") || name.EndsWith(value: "Manager"))
        {
            return layerColours[4];
        }

        return isInternal ? layerColours[5] : layerColours[6];
    }
}