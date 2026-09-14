using System.Linq;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Orchestrations.Rendering;
using Xunit;
namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public class CallChainViewTests
{
    [Fact]
    public async Task ShouldShowPublicRecursionAsAFiniteLoop()
    {
        var project=await ConcreteCallChainTests.ExtractAsync("public class A { public void Run() { Run(); } }");
        var model=TestServices.Get<IContextualLayoutOrchestrationService>().BuildRenderModel(new RenderModel([project],diagramType:DiagramTypes.CallChain));
        var edge=Assert.Single(model.CrossProjectConnections);
        Assert.Equal(edge.SourceId,edge.TargetId);
        Assert.True(edge.Points.Select(p=>p.X).Distinct().Count()>1);
        Assert.True(edge.Points.Select(p=>p.Y).Distinct().Count()>1);
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
        Assert.Contains(model.CrossProjectConnections,e=>e.FromType=="Entry"&&e.ToType=="Worker");
        Assert.Contains(model.CrossProjectConnections,e=>e.FromType=="Worker"&&e.ToType=="System.IO.File");
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
        Assert.Contains(model.CrossProjectConnections,e=>e.FromType=="Worker"&&e.ToType=="System.IO.File");
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
        foreach(var edge in model.CrossProjectConnections)
        foreach(var obstacle in model.Projects.Where(p=>!p.Nodes.Any(n=>n.Id==edge.SourceId||n.Id==edge.TargetId)))
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
        Assert.True(entry.X+entry.Width < worker.X);
        Assert.True(worker.X+worker.Width < file.X);
        Assert.Contains(model.CrossProjectConnections,e=>e.FromType=="Entry"&&e.ToType=="Worker");
        Assert.Contains(model.CrossProjectConnections,e=>e.FromType=="Worker"&&e.ToType=="System.IO.File");
        Assert.All(model.CrossProjectConnections.SelectMany(e=>e.Points),point=>Assert.True(point.Y>=40));
    }
}
