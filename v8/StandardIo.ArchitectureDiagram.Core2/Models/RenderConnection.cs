// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
namespace StandardIo.ArchitectureDiagram.Core2.Models;
public sealed record RenderConnection(string Id, string SourceId, string TargetId, string FromType, string ToType, bool Inheritance, DrawingPoint[] Points, string Stroke = "#d1d5db")
{
    public string? Label { get; init; }
    public bool IsComposition { get; init; }
}