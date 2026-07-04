using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Models;

internal static class DiagramSettingsFactory
{
    public static DiagramSettings CreateDefault()
    {
        return new DiagramSettings
        {
            Canvas = CreateCanvasSettings(),
            Layout = new LayoutSettings(),
            ExcludedNamespaces = new List<string> { "*.Migrations", "*.Generated", "*.Tests" },
            ExcludedNames = new List<string> { "*Designer", "*Generated*" },
            StyleRules = CreateStyleRules(),
            OutputRenderer = DiagramRendererIds.Drawio,
            ExternalDependencyTag = "[External]"
        };
    }

    private static CanvasSettings CreateCanvasSettings() =>
        new() { BackgroundColor = "#111111", DefaultFontColor = "#ffffff" };

    private static List<StyleRule> CreateStyleRules() =>
        new()
        {
            Rule("*Controller", "#1f57ff", "#102a86", "#ffffff"),
            Rule("*AggregationService", "#2f8f83", "#16564f", "#ffffff"),
            Rule("*CoordinationService", "#2f8f83", "#16564f", "#ffffff"),
            Rule("*OrchestrationService", "#48aeea", "#1f6f95", "#111111"),
            Rule("*ProcessingService", "#651fff", "#35118a", "#ffffff"),
            Rule("*FoundationService", "#2daf11", "#1a7009", "#111111"),
            Rule("*Broker", "#72bf1e", "#467914", "#111111"),
            Rule("*Repository", "#4b0000", "#ffffff", "#ffffff", "cylinder"),
            Rule("*Db", "#4b0000", "#ffffff", "#ffffff", "cylinder"),
            Rule("*Database", "#4b0000", "#ffffff", "#ffffff", "cylinder"),
            Rule("*Hub", "#f36c21", "#a43b08", "#111111", "rhombus"),
            Rule("*Queue", "#f36c21", "#a43b08", "#111111", "rhombus"),
            Rule("*Service", "#dae8fc", "#6c8ebf", "#111111")
        };

    private static StyleRule Rule(
        string match,
        string fillColor,
        string strokeColor,
        string fontColor,
        string shape = "rounded") =>
        new()
        {
            Match = match,
            Style = new NodeStyle
            {
                FillColor = fillColor,
                StrokeColor = strokeColor,
                FontColor = fontColor,
                Shape = shape,
                Shadow = true
            }
        };
}
