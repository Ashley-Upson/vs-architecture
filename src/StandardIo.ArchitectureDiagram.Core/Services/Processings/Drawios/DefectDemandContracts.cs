using System;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class DefectDemandContracts
{
    public static DefectDemandContract For(HardGeometryDefectKind defect) => defect switch
    {
        HardGeometryDefectKind.NodeCollision => new(
            defect, DefectResolutionKind.LinkSegmentDemandAlternatives,
            new[] { LinkSegmentRole.ObstacleBypass }, true,
            "Produce viable-side obstacle bypass rail alternatives."),
        HardGeometryDefectKind.SharedSegment => new(
            defect, DefectResolutionKind.LinkSegmentDemandAlternatives,
            new[] { LinkSegmentRole.Through }, true,
            "Produce distinct parallel rail demands for the competing routes."),
        HardGeometryDefectKind.ReusedBend => new(
            defect, DefectResolutionKind.LinkSegmentDemandAlternatives,
            new[] { LinkSegmentRole.TurnTransition }, true,
            "Produce distinct turn or transition rail demands."),
        HardGeometryDefectKind.SpacingDeficit => new(
            defect, DefectResolutionKind.IncreasedExtentDemand,
            Array.Empty<LinkSegmentRole>(), true,
            "Increase the rail or rectangle extent requirement."),
        HardGeometryDefectKind.NonOrthogonalSegment => new(
            defect, DefectResolutionKind.RejectTopologyAndRegenerate,
            Array.Empty<LinkSegmentRole>(), false,
            "Reject the current topology and request orthogonal regeneration."),
        HardGeometryDefectKind.ImmediateReversal => new(
            defect, DefectResolutionKind.RejectTopologyAndRegenerate,
            Array.Empty<LinkSegmentRole>(), false,
            "Reject the current topology and request regeneration."),
        _ => throw new ArgumentOutOfRangeException(nameof(defect))
    };
}
