// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
namespace StandardIo.ArchitectureDiagram.Core2.Models;
public sealed record DrawingPoint(double X, double Y);
internal sealed record DrawingRoute(TypeRelationship Relationship, DrawingPoint[] Points);