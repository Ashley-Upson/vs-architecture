using System;
using System.Collections.Generic;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
internal abstract class ArchitectureLayoutRuleProcessingService : ILayoutRuleProcessingService
{
    public void ApplyRule(RenderModel model) { if(model.DiagramType==DiagramTypes.Architecture) ApplyArchitectureRule(model); }
    public IEnumerable<string> GetViolations(RenderModel model) => model.DiagramType==DiagramTypes.Architecture?GetArchitectureViolations(model):Array.Empty<string>();
    protected abstract void ApplyArchitectureRule(RenderModel model);
    protected virtual IEnumerable<string> GetArchitectureViolations(RenderModel model) => Array.Empty<string>();
}
