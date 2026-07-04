namespace StandardIo.ArchitectureDiagram.Core.Models;

public sealed class NodeStyle
{
    public string FillColor { get; set; } = "#dae8fc";
    public string StrokeColor { get; set; } = "#6c8ebf";
    public string FontColor { get; set; } = "#111111";
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
