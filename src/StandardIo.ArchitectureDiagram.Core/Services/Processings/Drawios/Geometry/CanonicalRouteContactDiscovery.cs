using System;
using System.Collections.Generic;
using System.Linq;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

internal sealed record RouteContactFact(
    string FirstRouteId,
    string SecondRouteId,
    CanonicalContact Contact,
    Segment FirstSegment,
    Segment SecondSegment);

internal static class CanonicalRouteContactDiscovery
{
    public static IReadOnlyList<RouteContactFact> Discover(
        LinkLayout first,
        LinkLayout second,
        int requiredParallelSpacing)
    {
        var firstSegments = Segments(first);
        var secondSegments = Segments(second);
        var facts = new List<RouteContactFact>();
        foreach (var left in firstSegments)
        foreach (var right in secondSegments)
        {
            var contact = CanonicalContactClassifier.Classify(left.Contact, right.Contact, requiredParallelSpacing);
            if (contact.Kind == CanonicalContactKind.Disjoint) continue;
            facts.Add(new RouteContactFact(
                first.Link.Id, second.Link.Id, contact, left.Contact.Segment, right.Contact.Segment));
        }

        return facts
            .OrderBy(item => item.Contact.Point?.X ?? int.MinValue)
            .ThenBy(item => item.Contact.Point?.Y ?? int.MinValue)
            .ThenBy(item => item.Contact.Kind)
            .ThenBy(item => item.FirstSegment.Start.X).ThenBy(item => item.FirstSegment.Start.Y)
            .ThenBy(item => item.SecondSegment.Start.X).ThenBy(item => item.SecondSegment.Start.Y)
            .ToArray();
    }

    private static IReadOnlyList<IndexedContactSegment> Segments(LinkLayout link)
    {
        var points = new[] { link.SourcePoint }.Concat(link.Points).Concat(new[] { link.TargetPoint }).ToArray();
        var raw = Enumerable.Range(0, Math.Max(0, points.Length - 1))
            .Select(index => new Segment(points[index], points[index + 1])).ToArray();
        return raw.Select((segment, index) => new IndexedContactSegment(new ContactSegment(
            segment,
            index > 0 ? raw[index - 1] : null,
            index + 1 < raw.Length ? raw[index + 1] : null,
            StartIsTerminal: index == 0,
            EndIsTerminal: index == raw.Length - 1))).ToArray();
    }

    private readonly record struct IndexedContactSegment(ContactSegment Contact);
}
