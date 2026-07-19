using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal static class GeneralDownwardLinkSegmentDemandProducer
{
    public static GeneralDownwardObservationReport Observe(
        IEnumerable<AdjacentDownwardLinkContext> source,
        int requiredClearance = 0)
    {
        var timer = Stopwatch.StartNew();
        var routes = source.OrderBy(item => item.Route.Link.Id, StringComparer.Ordinal).Select(context =>
        {
            var canonical = LogicalRouteNormalizer.NormalizePoints(
                new[] { context.Route.SourcePoint }.Concat(context.Route.Points).Concat(new[] { context.Route.TargetPoint }));
            var rejection = Rejection(context);
            if (rejection is not null)
                return new GeneralDownwardLinkPlan(
                    context.Route.Link.Id, false, rejection, Array.Empty<LinkSegmentDemand>(), Array.Empty<VerticalLinkColumnDemand>(),
                    context.Route.Link.SourceId, context.Route.Link.TargetId, canonical, new[] { rejection.Value.ToString() });
            var crossed = context.InterLayerAxisRanges.Keys.Where(id =>
                    id.UpperLayer >= context.Source.Depth && id.LowerLayer <= context.Target.Depth)
                .OrderBy(id => id.UpperLayer).ThenBy(id => id.LowerLayer).ToArray();
            if (crossed.Length != context.Target.Depth - context.Source.Depth)
                return new GeneralDownwardLinkPlan(
                    context.Route.Link.Id, false, AdjacentDownwardRejectionReason.MultipleInterLayer,
                    Array.Empty<LinkSegmentDemand>(), Array.Empty<VerticalLinkColumnDemand>(),
                    context.Route.Link.SourceId, context.Route.Link.TargetId, canonical,
                    new[] { "A semantic crossed inter-layer is unavailable in the current placement revision." });
            var produced = DownwardLinkSegmentDemandFactory.Create(context, crossed, requiredClearance);
            return new GeneralDownwardLinkPlan(
                context.Route.Link.Id, true, null, produced.SegmentDemands, produced.VerticalColumnDemands,
                context.Route.Link.SourceId, context.Route.Link.TargetId, canonical, Array.Empty<string>());
        }).ToArray();
        timer.Stop();
        return new GeneralDownwardObservationReport(routes,
            timer.ElapsedTicks * 1_000_000 / Stopwatch.Frequency);
    }

    private static AdjacentDownwardRejectionReason? Rejection(AdjacentDownwardLinkContext context)
    {
        if (context.ExposureTreeSpecific) return AdjacentDownwardRejectionReason.ExposureTreeSpecific;
        if (context.Target.Depth <= context.Source.Depth)
            return context.Target.Depth == context.Source.Depth
                ? AdjacentDownwardRejectionReason.SameLayer : AdjacentDownwardRejectionReason.UpwardOrReturn;
        if (context.Route.ExitY != 1 || context.Route.EntryY != 0 ||
            context.Route.SourcePoint.Y != context.Source.Rect.Bottom || context.Route.TargetPoint.Y != context.Target.Rect.Y)
            return AdjacentDownwardRejectionReason.UnsupportedConnectionTopology;
        if (context.LayoutRevision != context.InterLayerAxisRanges.Keys.Select(item => item.LayoutRevision)
                .DefaultIfEmpty(context.LayoutRevision).First())
            return AdjacentDownwardRejectionReason.RevisionMismatch;
        return null;
    }
}
