using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
internal sealed class ProjectParentCentringLayoutRuleProcessingService(ParentCentringLayoutRuleProcessingService rule) : ArchitectureLayoutRuleProcessingService
{
    protected override void ApplyArchitectureRule(RenderModel model) => ProjectLayout.Apply(model,rule.ApplyRule);
}
