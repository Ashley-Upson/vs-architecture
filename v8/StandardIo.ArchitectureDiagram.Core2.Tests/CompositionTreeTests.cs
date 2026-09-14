using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
using Xunit;
namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public class CompositionTreeTests
{
    [Fact]
    public void ShouldNotExpandRuntimeCallsAsCompositionReferences()
    {
        DefinedType Type(string name) => new() { Name=name,IsInternal=true,HasDeclaredBehaviour=true,Methods=[new() {Name="Run"}] };
        var project=new ProjectModel { Name="P",Types=[Type("Caller"),Type("Runtime")],Dependencies=[new() {FromType="Caller",ToType="Runtime",FromMethod="Run",DependencyType=DependencyType.Consumed}] };
        var trees=TestServices.Get<ICompositionTreeService>().Build(new RenderModel([project]));
        var caller=trees.Single(t=>t.Title=="P / Caller");
        Assert.DoesNotContain(caller.Nodes,n=>n.TypeName=="Runtime");
    }
    [Fact]
    public async Task ShouldRetainConstructorAndReferenceOnlyMethodAttribution()
    {
        var model = await ConcreteCallChainTests.ExtractAsync("""
            public class Child { public void Run() {} }
            public class Root { public Root(Child child) {} public void Register() { _ = typeof(Child); } }
            """);
        var root = model.Types!.Single(t=>t.Name=="Root");
        Assert.Contains(root.CompositionMembers!,m=>m.Name==".ctor" && m.TypeNames.Contains("Child"));
        Assert.Contains(root.CompositionMembers!,m=>m.Name=="Register" && m.TypeNames.Contains("Child"));
    }
    [Fact]
    public void ShouldRepeatSharedTypesPerRootAndStopCircularExpansionInBothModes()
    {
        DefinedType Type(string name,params string[] refs) => new() { Name=name,IsInternal=true,HasDeclaredBehaviour=true,Methods=[new() { Name="Run" }],CompositionMembers=[new("Run",refs)] };
        var project = new ProjectModel { Name="P",Types=[Type("A","Shared"),Type("B","Shared"),Type("Shared","Shared")] };
        string[]? expected=null;
        foreach(bool dedupe in new[]{false,true})
        {
            var model=new RenderModel([project],new RenderConfiguration { NoDuplicates=dedupe },DiagramTypes.Composition);
            var trees=TestServices.Get<ICompositionTreeService>().Build(model);
            Assert.Equal(2,trees.Length);
            foreach(var tree in trees) Assert.Contains(tree.Nodes,n=>n.Label=="Shared (circular reference)");
            var layout=TestServices.Get<ICompositionTreeLayoutService>().Layout(model,trees);
            Assert.Equal(2,layout.Projects.Length);
            foreach(var drawing in layout.Projects)
            {
                Assert.Equal(drawing.Nodes.Length-1,drawing.Connections.Length);
                foreach(var edge in drawing.Connections)
                {
                    var parent=drawing.Nodes.Single(n=>n.Id==edge.SourceId);var child=drawing.Nodes.Single(n=>n.Id==edge.TargetId);
                    Assert.True(child.Y>parent.Y);Assert.True(child.X>parent.X);
                    Assert.Equal(child.X,edge.Points[^1].X);Assert.Equal(child.Y+child.Height/2,edge.Points[^1].Y);
                    Assert.True(edge.IsTree);
                }
            }
            var labels=trees.SelectMany(t=>t.Nodes).Select(n=>n.Label).ToArray();
            if(expected!=null) Assert.Equal(expected,labels);expected=labels;
        }
    }
    [Fact]
    public void ShouldRenderEntityHeaderAndTypeBeforeMemberNameInBothFormats()
    {
        var project=new ProjectModel {Name="P",Types=[new() {Name="School",IsInternal=true,HasDeclaredBehaviour=false,Properties=[new() { Type="System.String",Name="Id" }]}]};
        var model=new RenderModel([project],diagramType:DiagramTypes.DataModel);
        var projection=TestServices.Get<IContextualModelService>().Prepare(model);
        Assert.Equal(new[]{"School","String : Id"},projection.Types[0].Lines);
        var layout=TestServices.Get<IContextualLayoutService>().Layout(model,projection);
        Assert.True(layout.Projects[0].Nodes[0].HasHeader);
        Assert.Contains("border-bottom",Encoding.UTF8.GetString(TestServices.Get<IDrawIODocumentService>().Render(layout)));
        Assert.Contains("<line",Encoding.UTF8.GetString(TestServices.Get<IHtmlDocumentService>().Render(layout)));
    }
}
