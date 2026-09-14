using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
using StandardIo.ArchitectureDiagram.Core2.Services.Orchestrations.Rendering;
using Xunit;
namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public class CompositionTreeTests
{
    [Fact]
    public async Task ShouldDefineSharedPrivateMethodsOnceAndKeepCallsAsLeaves()
    {
        var project=await ConcreteCallChainTests.ExtractAsync("""
            using System;
            public class Other { public void Work() {} }
            public class Service {
                public void Run() { TryCatch(() => new Other().Work()); }
                public void Save() { TryCatch(() => new Other().Work()); }
                private void TryCatch(Action action) { action(); Recover(); }
                private void TryCatch(int value) { Recover(); }
                private void Recover() { Finish(); }
                private void Finish() { Recover(); }
            }
            """,frameworkReferences:true);
        foreach(bool dedupe in new[]{false,true})
        {
            var tree=TestServices.Get<ICompositionTreeService>().Build(new RenderModel([project],new RenderConfiguration {NoDuplicates=dedupe},DiagramTypes.Composition)).Single(t=>t.Title.EndsWith(" / Service"));
            var root=tree.Nodes.Single(n=>n.ParentId==null);
            var definitions=tree.Nodes.Where(n=>n.MemberId!=null).ToArray();
            Assert.All(definitions.GroupBy(n=>n.MemberId),g=>Assert.Single(g));
            Assert.All(definitions.Where(n=>n.Label!="Lambda Expression"),n=>Assert.Equal(root.Id,n.ParentId));
            Assert.Equal(2,definitions.Count(n=>n.Label=="TryCatch"));
            var actionTryCatch=definitions.Single(n=>n.Label=="TryCatch" && n.MemberId!.Contains("Action"));
            foreach(string name in new[]{"Run","Save"})
            {
                var method=definitions.Single(n=>n.Label==name);
                var call=Assert.Single(tree.Nodes,n=>n.ParentId==method.Id&&n.TargetMemberId==actionTryCatch.MemberId);
                Assert.DoesNotContain(tree.Nodes,n=>n.ParentId==call.Id);
            }
            var recover=definitions.Single(n=>n.Label=="Recover");
            Assert.Contains(tree.Nodes,n=>n.ParentId==actionTryCatch.Id&&n.TargetMemberId==recover.MemberId);
            Assert.Equal(2,tree.Nodes.Count(n=>n.Label=="Other → Work"));
        }
    }
    [Fact]
    public async Task ShouldLinkOverloadsToTheirExactMethodAndBoundLocalRecursion()
    {
        var project = await ConcreteCallChainTests.ExtractAsync("""
            public class B { public void Work(int value) {} public void Work(string value) {} }
            public class A { public void Run() { var b = new B(); b.Work(1); b.Work("x"); Repeat(); } private void Repeat() { Repeat(); } }
            """);
        var model = new RenderModel([project], diagramType: DiagramTypes.CallChain);
        var trees = TestServices.Get<ICompositionTreeService>().Build(model);
        var calls = trees.SelectMany(t => t.Nodes).Where(n => n.Label == "B → Work").ToArray();
        Assert.Equal(2, calls.Length);
        Assert.Equal(2, calls.Select(n => n.TargetMemberId).Distinct().Count());
        var layout = TestServices.Get<ICompositionTreeLayoutService>().Layout(model, trees);
        foreach (var call in calls)
        {
            var target = trees.SelectMany(t => t.Nodes).Single(n => n.TypeName == "B" && n.MemberId == call.TargetMemberId);
            Assert.Contains(layout.CrossProjectConnections, e => e.SourceId == call.Id && e.TargetId == target.Id);
        }
        var composition = TestServices.Get<ICompositionTreeService>().Build(new RenderModel([project], diagramType: DiagramTypes.Composition));
        Assert.Contains(composition.SelectMany(t => t.Nodes), n => n.Label == "A → Repeat");
        Assert.True(composition.Sum(t => t.Nodes.Length) < 30);
    }
    [Fact]
    public async Task ShouldKeepEachTypeInItsOwnContainerAndConnectCallChainMethods()
    {
        var project = await ConcreteCallChainTests.ExtractAsync("""
            public class B { public string Work(int value) => value.ToString(); }
            public class A {
                private B b;
                public A(B b) { this.b = b; }
                public void Run() { Helper(); }
                private void Helper() { b.Work(1); }
            }
            """);
        var trees = TestServices.Get<ICompositionTreeService>().Build(new RenderModel([project], diagramType: DiagramTypes.Composition));
        Assert.Equal(2, trees.Length);
        var a = trees.Single(t => t.Title.EndsWith(" / A"));
        Assert.DoesNotContain(a.Nodes, n => n.Label == "Work");
        Assert.Contains(a.Nodes, n => n.Label == "B → Work");
        var layout = TestServices.Get<IContextualLayoutOrchestrationService>().BuildRenderModel(new RenderModel([project], diagramType: DiagramTypes.CallChain));
        Assert.Equal(2, layout.Projects.SelectMany(p=>p.Nodes).Count(n=>n.Label is "A" or "B"));
        Assert.NotEmpty(layout.CrossProjectConnections.Concat(layout.Projects.SelectMany(p=>p.Connections).Where(e=>!e.IsTree)));
        Assert.Contains(layout.Projects.SelectMany(p => p.Nodes), n => n.Label.Contains("Inputs") && n.Label.Contains("Int32 : value") && n.Label.Contains("Outputs") && n.Label.Contains("String"));
    }
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
    public void ShouldKeepReferencesAsLeavesAndRenderEachTypeOnceInBothModes()
    {
        DefinedType Type(string name,params string[] refs) => new() { Name=name,IsInternal=true,HasDeclaredBehaviour=true,Methods=[new() { Name="Run" }],CompositionMembers=[new("Run",refs)] };
        var project = new ProjectModel { Name="P",Types=[Type("A","Shared"),Type("B","Shared"),Type("Shared","Shared")] };
        string[]? expected=null;
        foreach(bool dedupe in new[]{false,true})
        {
            var model=new RenderModel([project],new RenderConfiguration { NoDuplicates=dedupe },DiagramTypes.Composition);
            var trees=TestServices.Get<ICompositionTreeService>().Build(model);
            Assert.Equal(3,trees.Length);
            foreach(var tree in trees) Assert.Contains(tree.Nodes,n=>n.Label=="Shared");
            var layout=TestServices.Get<ICompositionTreeLayoutService>().Layout(model,trees);
            Assert.Equal(3,layout.Projects.Length);
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
