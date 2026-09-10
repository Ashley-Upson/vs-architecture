// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System;

namespace StandardIo.ArchitectureDiagram.Core2.Models;

public sealed class DiagramGenerationRequest
{
    public string[] ProjectPaths { get; set; } = Array.Empty<string>();

    public DiagramTypes DiagramType { get; set; }
}