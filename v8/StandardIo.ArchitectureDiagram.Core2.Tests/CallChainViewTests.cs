using System.Linq;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Orchestrations.Rendering;
using Xunit;
namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public class CallChainViewTests
{
    private static RenderConnection[] Calls(RenderModel model) => model.CrossProjectConnections.Concat(model.Projects.SelectMany(p=>p.Connections).Where(e=>!e.IsTree)).ToArray();
    [Fact]
    public async Task ShouldRepeatOnlyConsumedMethodsAndGroupTreesByProject()
    {
        var project=await ConcreteCallChainTests.ExtractAsync("""
            public class A { public void Run() { new Shared().Used(); } }
            public class B { public void Run() { new Shared().Used(); } }
            public class Shared { public void Used() {} public void Other() {} }
            """);
        var model=TestServices.Get<IContextualLayoutOrchestrationService>().BuildRenderModel(new RenderModel([project],diagramType:DiagramTypes.CallChain));
        var box=Assert.Single(model.Projects);
        Assert.Equal(project.Name,box.Name);
        Assert.Equal(2,box.Nodes.Count(n=>n.TypeName=="Shared"&&n.Label.StartsWith("Used\n")));
        Assert.Single(box.Nodes,n=>n.TypeName=="Shared"&&n.Label.StartsWith("Other\n"));
        var byId=box.Nodes.ToDictionary(n=>n.Id);
        var calls=box.Connections.Where(e=>!e.IsTree).ToArray();
        Assert.Equal(2,calls.Length);
        Assert.All(calls,e=>Assert.True(byId[e.SourceId].X+byId[e.SourceId].Width<byId[e.TargetId].X));
    }
    [Fact]
    public async Task ShouldOmitInputsWhenMethodHasNoParameters()
    {
        var project=await ConcreteCallChainTests.ExtractAsync("public class A { public void Run() {} public void Accept(int value) {} }");
        var model=TestServices.Get<IContextualLayoutOrchestrationService>().BuildRenderModel(new RenderModel([project],diagramType:DiagramTypes.CallChain));
        var nodes=model.Projects.SelectMany(p=>p.Nodes).ToArray();
        Assert.DoesNotContain(nodes.Single(n=>n.Label.StartsWith("Run\n")).TextLines,l=>l.Text=="Inputs");
        Assert.Contains(nodes.Single(n=>n.Label.StartsWith("Accept\n")).TextLines,l=>l.Text=="Inputs");
    }
    [Fact]
    public async Task ShouldEndPublicRecursionWithAForwardTerminalCopy()
    {
        var project=await ConcreteCallChainTests.ExtractAsync("public class A { public void Run() { Run(); } }");
        var model=TestServices.Get<IContextualLayoutOrchestrationService>().BuildRenderModel(new RenderModel([project],diagramType:DiagramTypes.CallChain));
        var edge=Assert.Single(Calls(model));
        Assert.NotEqual(edge.SourceId,edge.TargetId);
        Assert.Contains(model.Projects.SelectMany(p=>p.Nodes),n=>n.Label.Contains("(recursive call)"));
        Assert.True(edge.Points.Select(p=>p.X).Distinct().Count()>1);
        Assert.True(edge.Points[^1].X>edge.Points[0].X);
    }
    [Fact]
    public async Task ShouldContinueInjectedInterfaceCallsThroughKnownImplementations()
    {
        var project=await ConcreteCallChainTests.ExtractAsync("""
            public interface IWorker { void Work(); }
            public class Worker : IWorker { public void Work() { System.IO.File.ReadAllText("file"); } }
            public class Entry { private IWorker worker; public Entry(IWorker worker) { this.worker=worker; } public void Run() { worker.Work(); } }
            """,frameworkReferences:true);
        var model=TestServices.Get<IContextualLayoutOrchestrationService>().BuildRenderModel(new RenderModel([project],diagramType:DiagramTypes.CallChain));
        Assert.Contains(Calls(model),e=>e.FromType=="Entry"&&e.ToType=="Worker");
        Assert.Contains(Calls(model),e=>e.FromType=="Worker"&&e.ToType=="System.IO.File");
    }
    [Fact]
    public async Task ShouldKeepExplicitContractsAndHidePrivateOverloads()
    {
        var project=await ConcreteCallChainTests.ExtractAsync("""
            public interface IWorker { void Work(); }
            public class Worker : IWorker {
                void IWorker.Work() { Work(1); }
                private void Work(int value) { System.IO.File.ReadAllText("file"); }
            }
            """,frameworkReferences:true);
        var model=TestServices.Get<IContextualLayoutOrchestrationService>().BuildRenderModel(new RenderModel([project],diagramType:DiagramTypes.CallChain));
        var worker=model.Projects.Single(p=>p.Nodes.Any(n=>n.TypeName=="Worker"));
        Assert.Equal(2,worker.Nodes.Length);
        Assert.Contains(worker.Nodes,n=>n.Label.StartsWith("IWorker.Work"));
        Assert.Contains(Calls(model),e=>e.FromType=="Worker"&&e.ToType=="System.IO.File");
    }
    [Fact]
    public async Task ShouldRoutePastInterveningContainersWithoutCrossingThem()
    {
        var project=await ConcreteCallChainTests.ExtractAsync("""
            public class A { public void Run() { new B().Run(); new D().Run(); } }
            public class B { public void Run() { new C().Run(); } }
            public class C { public void Run() { new D().Run(); } }
            public class D { public void Run() {} }
            """);
        var model=TestServices.Get<IContextualLayoutOrchestrationService>().BuildRenderModel(new RenderModel([project],diagramType:DiagramTypes.CallChain));
        var box=Assert.Single(model.Projects);
        foreach(var edge in Calls(model))
        foreach(var obstacle in box.Nodes.Where(n=>n.Id!=edge.SourceId&&n.Id!=edge.TargetId))
        for(int i=1;i<edge.Points.Length;i++)
        {
            var a=edge.Points[i-1];var b=edge.Points[i];
            bool crosses=a.Y==b.Y ? a.Y>obstacle.Y&&a.Y<obstacle.Y+obstacle.Height&&System.Math.Max(a.X,b.X)>obstacle.X&&System.Math.Min(a.X,b.X)<obstacle.X+obstacle.Width :
                a.X>obstacle.X&&a.X<obstacle.X+obstacle.Width&&System.Math.Max(a.Y,b.Y)>obstacle.Y&&System.Math.Min(a.Y,b.Y)<obstacle.Y+obstacle.Height;
            Assert.False(crosses);
        }
    }
    [Fact]
    public async Task ShouldTracePrivateHelpersButOnlyDrawPublicBoundariesLeftToRight()
    {
        var project = await ConcreteCallChainTests.ExtractAsync("""
            using System;
            public class Entry {
                public void Run() { TryCatch(() => new Worker().Work()); }
                private void TryCatch(Action action) { action(); }
            }
            public class Worker {
                public void Work() { Helper(); }
                private void Helper() { System.IO.File.ReadAllText("file"); }
            }
            """, frameworkReferences: true);
        var model = TestServices.Get<IContextualLayoutOrchestrationService>().BuildRenderModel(new RenderModel([project], diagramType: DiagramTypes.CallChain));
        var labels = model.Projects.SelectMany(p=>p.Nodes).Select(n=>n.Label).ToArray();
        Assert.DoesNotContain(labels, n=>n.Contains("TryCatch") || n.Contains("Helper") || n.Contains("Lambda Expression") || n.Contains("Constructor"));
        var entry = model.Projects.Single(p=>p.Nodes.Any(n=>n.TypeName=="Entry"));
        var worker = model.Projects.Single(p=>p.Nodes.Any(n=>n.TypeName=="Worker"));
        var file = model.Projects.Single(p=>p.Nodes.Any(n=>n.TypeName=="System.IO.File"));
        Assert.True(entry.Nodes.First(n=>n.TypeName=="Entry").X < worker.Nodes.First(n=>n.TypeName=="Worker").X);
        Assert.True(worker.X+worker.Width < file.X);
        Assert.Contains(Calls(model),e=>e.FromType=="Entry"&&e.ToType=="Worker");
        Assert.Contains(Calls(model),e=>e.FromType=="Worker"&&e.ToType=="System.IO.File");
        Assert.All(model.CrossProjectConnections.SelectMany(e=>e.Points),point=>Assert.True(point.Y>=40));
    }
}
