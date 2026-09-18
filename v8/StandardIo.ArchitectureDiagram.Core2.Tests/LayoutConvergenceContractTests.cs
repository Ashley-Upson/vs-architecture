using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
using Xunit;
namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public class LayoutConvergenceContractTests
{
    [Fact]
    public void ValidButChangingModelMustContinueUntilUnchanged()
    {
        var rule = new SettlingRule();
        var service = Service(rule);
        var model = new RenderModel(100,100,[]);
        service.Layout(model);
        Assert.Equal(103,model.Width);
        Assert.Equal(4,rule.Calls);
    }
    [Fact]
    public void ValidButChangingModelMustNotSucceedAtIterationLimit()
    {
        var model = new RenderModel(100,100,[]);
        model.Configuration.MaxLayoutIterations=2;
        var error=Assert.Throws<InvalidOperationException>(()=>Service(new SettlingRule()).Layout(model));
        Assert.Contains(nameof(SettlingRule),error.Message);
    }
    [Fact]
    public void ContradictingRulesMustNotCountAsCompletionWhenTheirChangesCancel()
    {
        var model=new RenderModel(100,100,[]);
        model.Configuration.MaxLayoutIterations=4;
        Assert.Throws<InvalidOperationException>(()=>Service(new SetWidthRule(200),new SetWidthRule(100)).Layout(model));
    }
    [Fact]
    public void EquivalentRecordReplacementMustCountAsUnchanged()
    {
        var model=new RenderModel(100,100,[new RenderProject("p","P",0,0,100,100,[],[])]) { LayoutInitialized=true };
        var rule=new EquivalentReplacementRule();
        Service(rule).Layout(model);
        Assert.Equal(1,rule.Calls);
    }
    [Fact]
    public void StableButInvalidModelMustReportItsUnmetCondition()
    {
        var error=Assert.Throws<InvalidOperationException>(()=>Service(new InvalidRule()).Layout(new RenderModel(100,100,[])));
        Assert.Contains(nameof(InvalidRule),error.Message);
        Assert.Contains("Required condition",error.Message);
    }
    private sealed class EquivalentReplacementRule : ILayoutRuleProcessingService
    {
        public int Calls;
        public void ApplyRule(RenderModel model)
        {
            Calls++;
            model.Projects=[model.Projects[0] with {Nodes=[],Connections=[]}];
        }
    }
    private sealed class InvalidRule : ILayoutRuleProcessingService
    {
        public void ApplyRule(RenderModel model) { }
        public System.Collections.Generic.IEnumerable<string> GetViolations(RenderModel model) => ["Required condition"];
    }
    [Fact]
    public void SharedAlignmentMustNotReopenAClearedDeepPassage()
    {
        RenderNode Node(string id,double x,double y)=>new(id,id,id,"blue",x,y,100,60,[]);
        RenderConnection Edge(string from,string to)=>new(from+to,from,to,from,to,false,[]);
        var project=new RenderProject("p","P",0,0,1200,600,
            [Node("deep",400,0),Node("first",0,160),Node("second",800,160),Node("shared",400,320),Node("leaf",800,480)],
            [Edge("deep","leaf"),Edge("first","shared"),Edge("second","shared")]);
        var model=new RenderModel(1200,600,[project]);
        var before=LayoutPassages.Blocked(model,project);
        new SharedChainAlignmentLayoutRuleProcessingService().ApplyRule(model);
        Assert.True(LayoutPassages.Blocked(model,project).IsSubsetOf(before));
    }
    private static IProjectModelLayoutService Service(params ILayoutRuleProcessingService[] rules)
    {
        var services=new ServiceCollection().AddArchitectureDiagram();
        services.RemoveAll<ILayoutRuleProcessingService>();
        foreach(var rule in rules) services.AddSingleton(rule);
        return services.BuildServiceProvider().GetRequiredService<IProjectModelLayoutService>();
    }
    private sealed class SettlingRule : ILayoutRuleProcessingService
    {
        public int Calls;
        public void ApplyRule(RenderModel model) { Calls++; if(model.Width<103) model.Width++; }
    }
    private sealed class SetWidthRule(double value) : ILayoutRuleProcessingService
    {
        public void ApplyRule(RenderModel model)=>model.Width=value;
    }
}
