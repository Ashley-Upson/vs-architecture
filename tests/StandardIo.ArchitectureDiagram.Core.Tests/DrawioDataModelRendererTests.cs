using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Models.DataModels;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.DataModels;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class DrawioDataModelRendererTests
{
    [Fact]
    public void Render_accepts_standalone_data_model_and_returns_page_content()
    {
        var sourceProperty = new DataModelProperty(
            "property-a-b", "entity-a", "Target", "Fixture.B", "entity-b", "Public",
            false, true, true, false, false, false, null, "Fixture.A.Target");
        var model = new DataModelDiagram(
            [
                Entity("entity-a", "A", [sourceProperty]),
                Entity("entity-b", "B", [])
            ],
            [new DataModelRelationship(
                "relationship-a-b", "entity-a", "entity-b", sourceProperty.Id,
                DataModelRelationshipKind.PropertyReference, false, false,
                "Property Fixture.A.Target references Fixture.B.", "Fixture.A.Target->Fixture.B")],
            [new DataModelDiagnostic("fixture", "Standalone model")]);

        var page = new DrawioDataModelRenderer().Render(model, new DataModelRenderSettings());

        Assert.Equal("Data Model", page.SuggestedName);
        Assert.Equal("data-model", page.StablePageKey);
        Assert.Contains(page.GraphModel.Descendants("mxCell"), cell => (string?)cell.Attribute("id") == "data_model_entity-a");
        Assert.Contains(page.GraphModel.Descendants("mxCell"), cell => (string?)cell.Attribute("id") == "data_model_entity-b");
        Assert.Single(page.GraphModel.Descendants("mxCell"), cell =>
            (string?)cell.Attribute("id") == "data_model_relationship_relationship-a-b");
        Assert.Single(page.Diagnostics);
    }

    [Fact]
    public void Render_is_byte_deterministic_for_reversed_model_enumeration()
    {
        var a = Entity("a", "A", []);
        var b = Entity("b", "B", []);
        var renderer = new DrawioDataModelRenderer();

        var first = renderer.Render(new DataModelDiagram([a, b], [], []), new DataModelRenderSettings());
        var second = renderer.Render(new DataModelDiagram([b, a], [], []), new DataModelRenderSettings());

        Assert.Equal(first.GraphModel.ToString(), second.GraphModel.ToString());
    }

    private static DataModelEntity Entity(string id, string name, IReadOnlyList<DataModelProperty> properties) =>
        new(id, "project", "Fixture", name, "Fixture." + name, "Fixture", "Class", "Public",
            false, false, false, null, "Public instance properties and no public instance methods.",
            "Fixture." + name, properties);
}
