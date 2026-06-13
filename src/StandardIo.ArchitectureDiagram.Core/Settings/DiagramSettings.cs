using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Settings;

public sealed class DiagramSettings
{
    public int Version { get; set; } = 1;
    public CanvasSettings Canvas { get; set; } = new();
    public LayoutSettings Layout { get; set; } = new();
    public List<string> ExcludedNamespaces { get; set; } = new();
    public List<string> ExcludedNames { get; set; } = new();
    public List<StyleRule> StyleRules { get; set; } = new();
    public List<StyleOverride> Overrides { get; set; } = new();
    public NodeStyle ProjectContainerStyle { get; set; } = NodeStyle.ProjectContainer();
    public NodeStyle ExternalDependencyStyle { get; set; } = NodeStyle.External();
    public ConnectorStyle Connector { get; set; } = new();

    public static DiagramSettings CreateDefault()
    {
        return new DiagramSettings
        {
            Canvas = new CanvasSettings
            {
                BackgroundColor = "#111111",
                DefaultFontColor = "#ffffff"
            },
            Layout = new LayoutSettings(),
            ExcludedNamespaces = new List<string>
            {
                "*.Migrations",
                "*.Generated",
                "*.Tests"
            },
            ExcludedNames = new List<string>
            {
                "*Designer",
                "*Generated*"
            },
            StyleRules = new List<StyleRule>
            {
                new() { Match = "*Controller", Style = new NodeStyle { FillColor = "#1f57ff", StrokeColor = "#102a86", FontColor = "#ffffff", Shape = "rounded", Shadow = true } },
                new() { Match = "*Orchestration", Style = new NodeStyle { FillColor = "#48aeea", StrokeColor = "#1f6f95", FontColor = "#ffffff", Shape = "rounded", Shadow = true } },
                new() { Match = "*Processing", Style = new NodeStyle { FillColor = "#651fff", StrokeColor = "#35118a", FontColor = "#ffffff", Shape = "rounded", Shadow = true } },
                new() { Match = "*Foundation", Style = new NodeStyle { FillColor = "#2daf11", StrokeColor = "#1a7009", FontColor = "#ffffff", Shape = "rounded", Shadow = true } },
                new() { Match = "*Broker", Style = new NodeStyle { FillColor = "#72bf1e", StrokeColor = "#467914", FontColor = "#ffffff", Shape = "rounded", Shadow = true } },
                new() { Match = "*Repository", Style = new NodeStyle { FillColor = "#4b0000", StrokeColor = "#ffffff", FontColor = "#ffffff", Shape = "cylinder", Shadow = true } },
                new() { Match = "*Db", Style = new NodeStyle { FillColor = "#4b0000", StrokeColor = "#ffffff", FontColor = "#ffffff", Shape = "cylinder", Shadow = true } },
                new() { Match = "*Database", Style = new NodeStyle { FillColor = "#4b0000", StrokeColor = "#ffffff", FontColor = "#ffffff", Shape = "cylinder", Shadow = true } },
                new() { Match = "*Hub", Style = new NodeStyle { FillColor = "#f36c21", StrokeColor = "#a43b08", FontColor = "#111111", Shape = "rhombus", Shadow = true } },
                new() { Match = "*Queue", Style = new NodeStyle { FillColor = "#f36c21", StrokeColor = "#a43b08", FontColor = "#111111", Shape = "rhombus", Shadow = true } }
            }
        };
    }
}

public sealed class CanvasSettings
{
    public string BackgroundColor { get; set; } = "#111111";
    public string DefaultFontColor { get; set; } = "#ffffff";
}

public sealed class LayoutSettings
{
    public int NodeWidth { get; set; } = 200;
    public int NodeHeight { get; set; } = 80;
    public int HorizontalSpacing { get; set; } = 80;
    public int VerticalSpacing { get; set; } = 80;
    public int ContainerPadding { get; set; } = 40;
}

public sealed class StyleRule
{
    public string Match { get; set; } = "*";
    public NodeStyle Style { get; set; } = new();
}

public sealed class StyleOverride
{
    public string FullName { get; set; } = string.Empty;
    public NodeStyle Style { get; set; } = new();
}

public sealed class NodeStyle
{
    public string FillColor { get; set; } = "#dae8fc";
    public string StrokeColor { get; set; } = "#6c8ebf";
    public string FontColor { get; set; } = "#ffffff";
    public string Shape { get; set; } = "rounded";
    public bool Shadow { get; set; } = true;
    public string? ExtraStyle { get; set; }

    public static NodeStyle ProjectContainer() => new()
    {
        FillColor = "#323a40",
        StrokeColor = "#263238",
        FontColor = "#ffffff",
        Shape = "swimlane",
        Shadow = true,
        ExtraStyle = "swimlaneLine=0;startSize=34;horizontal=1;opacity=88;"
    };

    public static NodeStyle External() => new()
    {
        FillColor = "#f36c21",
        StrokeColor = "#a43b08",
        FontColor = "#111111",
        Shape = "rhombus",
        Shadow = true
    };
}

public sealed class ConnectorStyle
{
    public string StrokeColor { get; set; } = "#ffffff";
    public int StrokeWidth { get; set; } = 2;
    public bool Rounded { get; set; } = false;
}
