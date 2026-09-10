using System;
namespace StandardIo.ArchitectureDiagram.Core2.Models;
public sealed class DiagramRenderRequest
{
    public string[] ProjectPaths { get; set; } = Array.Empty<string>();
    public DiagramTypes DiagramType { get; set; }
    public string? Format { get; set; }
    public string OutputPath { get; set; } = "";
    public bool ShowHelp { get; set; }
}
