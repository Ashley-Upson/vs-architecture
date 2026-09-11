// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using StandardIo.ArchitectureDiagram.Core2.Models;

namespace StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Exports;
internal interface IRawJsonExportService
{
    byte[] Export(DiagramTypes diagramType, ProjectModel[] projects);
}
