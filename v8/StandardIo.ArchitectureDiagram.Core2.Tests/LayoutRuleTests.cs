using System;
using System.Linq;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Exposures;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
using Xunit;
namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public class LayoutRuleTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void ShouldCentreOverOuterChildEdgesWithUnequalWidthsAndGaps()
    {
        var model = CreateUncentredModel();
        var project = model.Projects[0];
        var parent = project.Nodes[0];
        var first = project.Nodes[1] with { X = 0, Width = 100 };
        var second = first with { Id = "second", TypeName = "Second", X = 300, Width = 300 };
        model.Projects[0] = project with
        {
            Nodes = new[] { parent, first, second },
            Connections = new[] { project.Connections[0], project.Connections[0] with { Id = "other", TargetId = "second", ToType = "Second" } }
        };
        new ParentCentringLayoutRuleProcessingService().ApplyRule(model);
        Assert.Equal(300, model.Projects[0].Nodes[0].X + parent.Width / 2);
        Assert.Equal(0, model.Projects[0].Nodes[1].X);
        Assert.Equal(300, model.Projects[0].Nodes[2].X);
    }

    [Fact]
    public void ShouldParseTheIterationLimit()
    {
        var parser = TestServices.Get<StandardIo.ArchitectureDiagram.Core2.Services.Processings.Commands.ICommandParserProcessingService>();
        var request = parser.Parse(new[] { "Architecture", "sample.csproj", "--output", "sample.html", "--max-layout-iterations", "25" });
        Assert.Equal(25, request.RenderConfiguration.MaxLayoutIterations);
        Assert.Equal(1000, new DiagramRenderRequest().RenderConfiguration.MaxLayoutIterations);
        Assert.Throws<ArgumentException>(() => parser.Parse(new[] { "Architecture", "sample.csproj", "--output", "sample.html", "--max-layout-iterations", "0" }));
    }

    [Fact]
    public void ShouldRepeatInjectedRulesUntilValidAndRespectTheIterationLimit()
    {
        var services = new ServiceCollection().AddArchitectureDiagram();
        services.RemoveAll<ILayoutRuleProcessingService>();
        var rule = new DelayedRule();
        services.AddSingleton<ILayoutRuleProcessingService>(rule);
        using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<IProjectModelLayoutService>();
        var model = CreateUncentredModel();
        model.Configuration.MaxLayoutIterations = 1;
        var error = Assert.Throws<InvalidOperationException>(() => service.Layout(model));
        Assert.Contains("Parent centring", error.Message);
        Assert.Equal(1, rule.Calls);
        model.Configuration.MaxLayoutIterations = 2;
        service.Layout(model);
        Assert.Equal(2, rule.Calls);
        model.Configuration.MaxLayoutIterations = 0;
        Assert.Throws<ArgumentOutOfRangeException>(() => service.Layout(model));
    }

    [Fact]
    public void ShouldLayoutFourHundredNodesWithSharedDependencies()
    {
        var types = Enumerable.Range(0, 400).Select(index => new DefinedType { Name = "Node" + index }).ToArray();
        var links = Enumerable.Range(0, 399).Where(index => index % 20 != 19)
            .Select(index => new TypeRelationship { FromType = "Node" + index, ToType = "Node" + (index + 1) }).ToList();
        links.AddRange(Enumerable.Range(0, 19).Select(index => new TypeRelationship { FromType = "Node" + (index * 20 + 19), ToType = "Node399" }));
        var watch = Stopwatch.StartNew();
        var model = TestServices.Get<LayoutModelBuilder>().BuildRenderModel(new RenderModel(new[] { new ProjectModel { Types = types, Dependencies = links.ToArray() } }));
        var project = Assert.Single(model.Projects);
        Assert.Equal(400, project.Nodes.Length);
        Assert.All(project.Connections, edge => Assert.True(project.Nodes.Single(node => node.Id == edge.TargetId).Y > project.Nodes.Single(node => node.Id == edge.SourceId).Y));
        foreach (var row in project.Nodes.GroupBy(node => node.Y))
        {
            var nodes = row.OrderBy(node => node.X).ToArray();
            for (int index = 1; index < nodes.Length; index++) Assert.True(nodes[index].X - nodes[index - 1].X >= 239.99);
        }
        output.WriteLine($"400 nodes, {project.Connections.Length} edges, {model.LayoutIterations} passes, {watch.ElapsedMilliseconds}ms");
    }

    private static RenderModel CreateUncentredModel()
    {
        var parent = new RenderNode("parent", "Parent", "Parent", "#003b99", 40, 60, 180, 60, Array.Empty<RenderText>());
        var child = parent with { Id = "child", TypeName = "Child", X = 280, Y = 220 };
        var edge = new RenderConnection("edge", "parent", "child", "Parent", "Child", false, Array.Empty<DrawingPoint>());
        return new RenderModel(600, 400, new[] { new RenderProject("project", "Project", 40, 40, 500, 350, new[] { parent, child }, new[] { edge }) });
    }

    private sealed class DelayedRule : ILayoutRuleProcessingService
    {
        public int Calls { get; private set; }
        public void ApplyRule(RenderModel renderModel)
        {
            Calls++;
            if (Calls >= 2)
            {
                var project = renderModel.Projects[0];
                project.Nodes[0] = project.Nodes[0] with { X = project.Nodes[1].X };
            }
        }
    }
}
