// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core2.Models;
internal sealed record DrawingNode(string Id, DefinedType Type, string Label, double X, double Y, double Width, double Height);
internal sealed record ProjectModelDrawing(string Id, ProjectModel Model, double X, double Width, double Height, IReadOnlyList<DrawingNode> Nodes);