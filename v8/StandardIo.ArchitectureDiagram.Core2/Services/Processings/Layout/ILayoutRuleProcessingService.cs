// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
internal interface ILayoutRuleProcessingService
{
    void ApplyRule(RenderModel renderModel);
    System.Collections.Generic.IEnumerable<string> GetViolations(RenderModel renderModel) => System.Array.Empty<string>();
}
