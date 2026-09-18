using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
internal sealed class LayerLinkReviewRuleProcessingService : ArchitectureLayoutRuleProcessingService
{
    protected override void ApplyArchitectureRule(RenderModel model) => LayerLinkClassification.Apply(model,
        edge=>edge.IsLayerViolation?edge with {Stroke="#ef4444"}:edge);
}
