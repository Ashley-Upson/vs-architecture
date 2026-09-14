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
    public async Task ShouldRetainOpenGenericTypeReferences()
    {
        var model = await ConcreteCallChainTests.ExtractAsync("""
            public class Root { public void Register() { _ = typeof(System.Collections.Generic.List<>); } }
            """);
        var member = model.Types!.Single(t => t.Name == "Root").CompositionMembers!.Single(m => m.Name == "Register");
        Assert.Contains(member.TypeNames, name => name.StartsWith("System.Collections.Generic.List<"));
    }
    [Fact]
    public async Task ShouldRetainCallsUsingAnonymousGenericArguments()
    {
        var model = await ConcreteCallChainTests.ExtractAsync("""
            public class Root {
                public void Run() { Consume(new { Value = 1 }); }
                private void Consume<T>(T value) {}
            }
            """);
        var member = model.Types!.Single(t => t.Name == "Root").CompositionMembers!.Single(m => m.Name == "Run");
        Assert.Contains(member.Calls!, c => c.TypeName == "Root" && c.MethodName == "Consume");
        Assert.All(member.TypeNames, name => Assert.False(string.IsNullOrWhiteSpace(name)));
    }
    [Fact]
    public async Task ShouldShowFrameworkExtensionAndLocalCallsUnderTheirActualLambdaOrMethod()
    {
        var project=await ConcreteCallChainTests.ExtractAsync("""
            using System;
            using System.Linq;
            public class Root {
                public void Run() {
                    Console.WriteLine("outside");
                    Action first = () => Console.WriteLine("inside");
                    Action second = () => { _ = new[] { 1, 2 }.Select(x => Math.Abs(x)).ToArray(); };
                    Helper();
                }
                private void Helper() { _ = string.Concat("a", "b"); }
            }
            """, frameworkReferences: true);
        var tree=TestServices.Get<ICompositionTreeService>().Build(new RenderModel([project])).Single(t=>t.Title.EndsWith(" / Root"));
        var lambdas=tree.Nodes.Where(n=>n.Label=="Lambda Expression").ToArray();
        Assert.Equal(3,lambdas.Length);
        Assert.Contains(tree.Nodes,n=>n.Label=="Console → WriteLine" && lambdas.Any(l=>l.Id==n.ParentId));
        Assert.Contains(tree.Nodes,n=>n.Label=="Enumerable → Select" && lambdas.Any(l=>l.Id==n.ParentId));
        Assert.Contains(tree.Nodes,n=>n.Label=="Math → Abs" && lambdas.Any(l=>l.Id==n.ParentId));
        Assert.Contains(tree.Nodes,n=>n.Label=="Root → Helper");
        var helper=Assert.Single(tree.Nodes,n=>n.Label=="Helper");
        Assert.Contains(tree.Nodes,n=>n.Label=="String → Concat" && n.ParentId==helper.Id);
    }
    [Fact]
    public async Task ShouldLabelAnonymousFunctionReferencesAsLambdaExpressions()
    {
        var model=await ConcreteCallChainTests.ExtractAsync("""
            public class Child { public void Run() {} }
            public class Root {
                public void Register() { System.Action callback = () => { System.Action nested = () => { _ = typeof(Child); }; }; }
            }
            """);
        var root=model.Types!.Single(t=>t.Name=="Root");
        var lambdaMembers=root.CompositionMembers!.Where(m=>m.Name=="").ToArray();
        Assert.Equal(2,lambdaMembers.Length);
        Assert.Contains(lambdaMembers,m=>m.TypeNames.SequenceEqual(new[]{"Child"}));
        Assert.Equal(2,lambdaMembers.Select(m=>m.Id).Distinct().Count());
        Assert.Contains(lambdaMembers,m=>lambdaMembers.Any(parent=>parent.Id==m.ParentId));
        var trees=TestServices.Get<ICompositionTreeService>().Build(new RenderModel([model]));
        Assert.Contains(trees.SelectMany(t=>t.Nodes),n=>n.Label=="Lambda Expression");
        Assert.All(trees.SelectMany(t=>t.Nodes),n=>Assert.False(string.IsNullOrWhiteSpace(n.Label)));
    }
    [Fact]
    public void ShouldOrderTreesLargestFirstWithoutWrappingOrChangingTheirInternalLayout()
    {
        CompositionTree Tree(string name, int count) => new(name,Enumerable.Range(0,count)
            .Select(i=>new CompositionTreeNode(name+i,name,name+i,i==0?0:1,i==0?null:name+"0")).ToArray());
        var trees=new[] { Tree("Small",2), Tree("Z",5), Tree("Large",12), Tree("A",5) };
        var model=new RenderModel([],diagramType:DiagramTypes.Composition);
        var service=TestServices.Get<ICompositionTreeLayoutService>();
        var layout=service.Layout(model,trees);
        Assert.Equal(new[]{"Large","A","Z","Small"},layout.Projects.Select(p=>p.Name));
        Assert.All(layout.Projects,p=>Assert.Equal(40,p.Y));
        for(int i=1;i<layout.Projects.Length;i++)
            Assert.Equal(layout.Projects[i-1].X+layout.Projects[i-1].Width+model.Configuration.Composition.ProjectSpacing,layout.Projects[i].X);
        foreach(var tree in trees)
        {
            var alone=service.Layout(new RenderModel([],diagramType:DiagramTypes.Composition),[tree]).Projects.Single();
            var grouped=layout.Projects.Single(p=>p.Name==tree.Title);
            Assert.Equal(alone.Nodes.Select(n=>(n.Id,n.Label,n.X,n.Y,n.Width,n.Height)),grouped.Nodes.Select(n=>(n.Id,n.Label,n.X,n.Y,n.Width,n.Height)));
            Assert.Equal(alone.Connections.SelectMany(e=>e.Points),grouped.Connections.SelectMany(e=>e.Points));
        }
        Assert.Equal(layout.Projects.Max(p=>p.Height)+80,layout.Height);
    }
    [Fact]
    public void ShouldShowRuntimeMethodDependenciesWithoutExpandingTheirCallChains()
    {
        DefinedType Type(string name) => new() { Name=name,IsInternal=true,HasDeclaredBehaviour=true,Methods=[new() {Name="Run"}] };
        var project=new ProjectModel { Name="P",Types=[Type("Caller"),Type("Runtime"),Type("Next")],Dependencies=[new() {FromType="Caller",ToType="Runtime",FromMethod="Run",ToMethod="Run",DependencyType=DependencyType.Consumed},new() {FromType="Runtime",ToType="Next",FromMethod="Run",ToMethod="Execute",DependencyType=DependencyType.Consumed}] };
        var trees=TestServices.Get<ICompositionTreeService>().Build(new RenderModel([project]));
        var caller=trees.Single(t=>t.Title=="P / Caller");
        var dependency=Assert.Single(caller.Nodes,n=>n.Label=="Runtime → Run");
        Assert.DoesNotContain(caller.Nodes,n=>n.ParentId==dependency.Id);
        Assert.DoesNotContain(caller.Nodes,n=>n.TypeName=="Next");
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
