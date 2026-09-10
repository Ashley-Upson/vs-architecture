// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;

namespace StandardIo.ArchitectureDiagram.Core2.Models;
public sealed class DiagramRenderRequest
{
    public string[] ProjectPaths { get; set; } = Array.Empty<string>();
    public RenderConfiguration RenderConfiguration { get; set; } = new();
    public DiagramTypes DiagramType { get; set; }
    public DiagramFormats Format { get; set; }
    public string OutputPath { get; set; } = "";
    public bool ShowHelp { get; set; }
}