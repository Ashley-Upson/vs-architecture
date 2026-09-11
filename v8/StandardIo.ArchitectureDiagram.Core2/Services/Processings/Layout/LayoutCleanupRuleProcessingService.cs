// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using StandardIo.ArchitectureDiagram.Core2.Models;

namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;

internal sealed class LayoutCleanupRuleProcessingService : ILayoutRuleProcessingService
{
    public void ApplyRule(RenderModel renderModel)
    {
        // Pack completed branches using the same ownership and clearance rules as
        // initial placement, allowing excess space to be reclaimed in either direction.
        BranchSpacingLayoutRuleProcessingService.ArrangeBranches(renderModel, reclaimSpace: true);
    }
}
