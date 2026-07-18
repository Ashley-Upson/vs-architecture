using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class CanonicalContactClassifierTests
{
    [Fact]
    public void Distinguishes_positive_overlap_from_endpoint_contact()
    {
        var overlap = CanonicalContactClassifier.Classify(
            Line(0, 10, 20, 10), Line(10, 10, 30, 10));
        var endpoint = CanonicalContactClassifier.Classify(
            Line(0, 10, 20, 10), Line(20, 10, 30, 10));

        Assert.Equal(CanonicalContactKind.PositiveCollinearOverlap, overlap.Kind);
        Assert.Equal(10, overlap.PositiveOverlap);
        Assert.Equal(CanonicalContactKind.StraightContinuation, endpoint.Kind);
    }

    [Fact]
    public void Clean_crossover_is_strict_interior_and_bend_free()
    {
        var contact = CanonicalContactClassifier.Classify(
            Line(0, 10, 20, 10), Line(10, 0, 10, 20));

        Assert.Equal(CanonicalContactKind.CleanPerpendicularCrossover, contact.Kind);
    }

    [Fact]
    public void Perpendicular_endpoint_is_not_a_clean_crossover()
    {
        var contact = CanonicalContactClassifier.Classify(
            Line(0, 10, 20, 10), Line(20, 10, 20, 20));

        Assert.Equal(CanonicalContactKind.EndpointToEndpoint, contact.Kind);
    }

    [Fact]
    public void Bend_involved_perpendicular_contact_is_not_clean()
    {
        var first = new ContactSegment(
            new Segment(new Point(0, 10), new Point(20, 10)),
            Next: new Segment(new Point(20, 10), new Point(20, 20)));
        var second = Line(20, 0, 20, 20);

        var contact = CanonicalContactClassifier.Classify(first, second);

        Assert.Equal(CanonicalContactKind.BendInvolvedPerpendicularContact, contact.Kind);
    }

    [Fact]
    public void Shared_bend_is_a_distinct_contact()
    {
        var first = new ContactSegment(
            new Segment(new Point(0, 10), new Point(20, 10)),
            Next: new Segment(new Point(20, 10), new Point(20, 20)));
        var second = new ContactSegment(
            new Segment(new Point(20, 10), new Point(30, 10)),
            Previous: new Segment(new Point(20, 0), new Point(20, 10)));

        var contact = CanonicalContactClassifier.Classify(first, second);

        Assert.Equal(CanonicalContactKind.SharedBend, contact.Kind);
    }

    [Fact]
    public void Near_parallel_segments_report_spacing_conflict()
    {
        var contact = CanonicalContactClassifier.Classify(
            Line(0, 10, 20, 10), Line(0, 15, 20, 15), requiredParallelSpacing: 12);

        Assert.Equal(CanonicalContactKind.NearParallelSpacingConflict, contact.Kind);
        Assert.Equal(5, contact.Separation);
    }

    private static ContactSegment Line(int x1, int y1, int x2, int y2) =>
        new(new Segment(new Point(x1, y1), new Point(x2, y2)));
}
