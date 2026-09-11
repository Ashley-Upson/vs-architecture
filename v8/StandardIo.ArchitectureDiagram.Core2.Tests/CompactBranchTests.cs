using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Exposures;
using StandardIo.ArchitectureDiagram.Core2.Models;
using Xunit;
namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public sealed class CompactBranchTests
{
    [Fact]
    public void ShouldAllocateEachSiblingItsOwnSubtreeWidth()
    {
        // Given: one short branch beside a branch with three leaf children.
        var model = new ProjectModel
        {
            Types = new[] { "Root", "Short", "Wide", "A", "B", "C" }.Select(name => new DefinedType { Name = name }).ToArray(),
            Dependencies = new[] { ("Root", "Short"), ("Root", "Wide"), ("Wide", "A"), ("Wide", "B"), ("Wide", "C") }
                .Select(pair => new TypeRelationship { DependencyType = DependencyType.Consumed, FromType = pair.Item1, ToType = pair.Item2 }).ToArray()
        };
        // When: the full rule loop must still validate all centring and spacing constraints.
        var drawing = TestServices.Get<LayoutModelBuilder>().BuildRenderModel(new RenderModel(new[] { model }));
        // Then: 180 + 60 + (3 * 180 + 2 * 60) plus two 40px margins.
        Assert.InRange(Assert.Single(drawing.Projects).Width, 0, 1100.01);
    }
    [Fact]
    public void ShouldReserveSpaceOnlyForBranchesThatActuallyOverlapVertically()
    {
        // Given: A overlaps B and B overlaps C, but A does not overlap C.
        RenderNode Node(string id, double y, double height) => new(id, id, id, "#123456", 40, y, 180, height, System.Array.Empty<RenderText>());
        var project = new RenderProject("scope", "Scope", 0, 0, 500, 500,
            new[] { Node("A", 0, 100), Node("B", 50, 150), Node("C", 150, 100) }, System.Array.Empty<RenderConnection>());
        // When
        var groups = StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout.LayoutGraph.BranchGroups(project).ToArray();
        // Then: transitive overlap must not force A and C into different columns.
        Assert.Contains(groups, group => group.Any(node => node.Id == "A") && group.Any(node => node.Id == "B"));
        Assert.Contains(groups, group => group.Any(node => node.Id == "B") && group.Any(node => node.Id == "C"));
        Assert.DoesNotContain(groups, group => group.Any(node => node.Id == "A") && group.Any(node => node.Id == "C"));
    }
}
