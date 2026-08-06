namespace StandardIo.ArchitectureDiagram.Core.Models;

public sealed class ConnectorStyle
{
    public string StrokeColor { get; set; } = "#ffffff";

    public int StrokeWidth { get; set; } = 2;

    public bool Rounded { get; set; } = false;

    public bool Dashed { get; set; } = false;

    public string? DashPattern { get; set; }

    public string StartArrow { get; set; } = "none";

    public string EndArrow { get; set; } = "block";

    public bool StartFill { get; set; } = true;

    public bool EndFill { get; set; } = true;

    public int ArrowSize { get; set; } = 1;

    public int Opacity { get; set; } = 100;

    public string FontColor { get; set; } = "#000000";

    public bool ShowLabels { get; set; } = false;

    public string? ExtraStyle { get; set; }
}
