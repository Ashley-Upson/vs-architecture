namespace StandardIo.ArchitectureDiagram.Core.Models;

public sealed class StyleRule
{
    public string Match { get; set; } = "*";

    public NodeStyle Style { get; set; } = new();
}
