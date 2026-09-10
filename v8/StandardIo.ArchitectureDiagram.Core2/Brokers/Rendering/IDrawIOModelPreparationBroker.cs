// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Brokers.Rendering;
internal interface IDrawIOModelPreparationBroker
{
    RenderModel PrepareRenderModel(RenderModel renderModel);
}