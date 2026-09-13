// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
namespace StandardIo.ArchitectureDiagram.Core2.Models;
public sealed record RenderProject(string Id, string Name, double X, double Y, double Width, double Height, RenderNode[] Nodes, RenderConnection[] Connections)
{
    public RenderNodeCategory[] ArchitecturalLayers { get; set; } = [];
}