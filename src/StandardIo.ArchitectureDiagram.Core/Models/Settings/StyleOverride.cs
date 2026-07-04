namespace StandardIo.ArchitectureDiagram.Core.Models;

public sealed class StyleOverride
{
    public string FullName { get; set; } = string.Empty;

    public NodeStyle Style { get; set; } = new();
}
