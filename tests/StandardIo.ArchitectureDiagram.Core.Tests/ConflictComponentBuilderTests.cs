using System.Linq;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class ConflictComponentBuilderTests
{
    [Fact]
    public void Independent_items_remain_separate()
    {
        var result = ConflictComponentBuilder.Build(new[] { "b", "a" }, item => item, new ConflictEdge[0]);

        Assert.Equal(new[] { "a", "b" }, result.Select(component => component.Id));
        Assert.All(result, component => Assert.Single(component.Members));
    }

    [Fact]
    public void Transitive_conflicts_form_one_component()
    {
        var result = ConflictComponentBuilder.Build(
            new[] { "a", "b", "c" }, item => item,
            new[] { new ConflictEdge("a", "b", "rail"), new ConflictEdge("b", "c", "turn") });

        var component = Assert.Single(result);
        Assert.Equal("a", component.Id);
        Assert.Equal(new[] { "a", "b", "c" }, component.Members);
        Assert.Equal(new[] { "rail", "turn" }, component.Causes);
    }

    [Fact]
    public void Reversed_enumeration_is_deterministic()
    {
        var items = new[] { "a", "b", "c", "d" };
        var edges = new[] { new ConflictEdge("c", "b", "x"), new ConflictEdge("d", "a", "y") };

        var forward = ConflictComponentBuilder.Build(items, item => item, edges);
        var reverse = ConflictComponentBuilder.Build(
            items.AsEnumerable().Reverse(), item => item, edges.AsEnumerable().Reverse());

        Assert.Equal(
            forward.Select(component => string.Join(",", component.Members)),
            reverse.Select(component => string.Join(",", component.Members)));
    }
}
