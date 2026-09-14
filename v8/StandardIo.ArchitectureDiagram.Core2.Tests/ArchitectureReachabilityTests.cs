using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Exposures;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Rendering;
using Xunit;
namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public class ArchitectureReachabilityTests
{
    private static ProjectModel Model(bool compositionRoot=false)=>new() {
        Name="P",Path="P.csproj",
        Types=new[]{"Root","Worker","Extension","Broker","External"}.Select(n=>new DefinedType {Name=n,IsInternal=true,HasDeclaredBehaviour=true,Methods=[new() {Name="Run"}]}).ToArray(),
        Dependencies=[new(){DependencyType=DependencyType.Consumed,FromType="Root",ToType="Worker",IsComposition=compositionRoot},
            new(){DependencyType=DependencyType.Consumed,FromType="Root",ToType="Extension",IsResultExtension=true},
            new(){DependencyType=DependencyType.Consumed,FromType="Extension",ToType="Broker"},new(){DependencyType=DependencyType.Consumed,FromType="Broker",ToType="External"}]};
    [Fact]
    public void ShouldGiveEachVisibleRootItsOwnContainerAndCopySharedDescendants()
    {
        var raw=new ProjectModel {Name="P",Path="P.csproj",
            Types=new[]{"Bootstrap","A","B","Shared"}.Select(n=>new DefinedType {Name=n,IsInternal=true,HasDeclaredBehaviour=true,Methods=[new(){Name="Run"}]}).ToArray(),
            Dependencies=[new(){FromType="Bootstrap",ToType="A",DependencyType=DependencyType.Consumed,IsComposition=true},
                new(){FromType="Bootstrap",ToType="B",DependencyType=DependencyType.Consumed,IsComposition=true},
                new(){FromType="A",ToType="Shared",DependencyType=DependencyType.Consumed},new(){FromType="B",ToType="Shared",DependencyType=DependencyType.Consumed}]};
        var trees=TestServices.Get<ProjectModelSplitter>().Split(raw);
        var model=TestServices.Get<LayoutModelBuilder>().BuildRenderModel(new RenderModel(trees,new RenderConfiguration {NoDuplicates=false}));
        Assert.Equal(2,model.Projects.Length);
        Assert.All(model.Projects,p=>{
            Assert.Equal(2,p.Nodes.Length);Assert.Contains(p.Nodes,n=>n.TypeName=="Shared");
            var edge=Assert.Single(p.Connections);var root=Assert.Single(p.Nodes,n=>!p.Connections.Any(e=>e.TargetId==n.Id));
            Assert.Equal(root.Id,edge.SourceId);
        });
        Assert.Empty(model.CrossProjectConnections);
    }
    [Fact]
    public void ShouldPruneBranchesDisconnectedByArchitectureFilteringAfterSplitting()
    {
        var raw=Model();var trees=TestServices.Get<ProjectModelSplitter>().Split(raw);
        var result=TestServices.Get<IProjectModelCompositionProcessingService>().Prepare(new RenderModel(trees,new RenderConfiguration {NoDuplicates=false}));
        var root=Assert.Single(result,p=>p.Model.Types!.Any(t=>t.Name=="Root"));
        Assert.Equal(new[]{"Root","Worker"},root.Model.Types!.Select(t=>t.Name).OrderBy(n=>n));
        Assert.Contains(root.Model.Dependencies!,d=>d.FromType=="Root"&&d.ToType=="Worker");
        Assert.Equal(5,raw.Types!.Length);
        var combined=TestServices.Get<IProjectModelCompositionProcessingService>().Prepare(new RenderModel([raw],new RenderConfiguration {NoDuplicates=true}));
        Assert.Contains(combined.SelectMany(p=>p.Model.Types!),t=>t.Name=="Broker");
    }
    [Fact]
    public void ShouldPreserveVisibleBranchesAsSeparateTreesWhenOriginalRootIsFilteredOut()
    {
        var raw=Model(true);raw.Dependencies![1].IsComposition=true;
        var trees=TestServices.Get<ProjectModelSplitter>().Split(raw);
        var result=TestServices.Get<IProjectModelCompositionProcessingService>().Prepare(new RenderModel(trees,new RenderConfiguration {NoDuplicates=false}));
        Assert.DoesNotContain(result.SelectMany(p=>p.Model.Types!),t=>t.Name=="Root");
        Assert.Contains(result,p=>p.Model.Types!.Length==1&&p.Model.Types[0].Name=="Worker");
        Assert.Contains(result,p=>p.Model.Types!.Any(t=>t.Name=="Broker")&&p.Model.Dependencies!.Any(d=>d.FromType=="Broker"&&d.ToType=="External"));
    }
}
