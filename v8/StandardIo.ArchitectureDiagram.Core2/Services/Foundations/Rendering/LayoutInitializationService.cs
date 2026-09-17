using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
internal interface ILayoutInitializationService { void Initialize(RenderModel model); }
internal sealed class LayoutInitializationService(
    DepthLayoutRuleProcessingService depth,
    ArchitecturalLayerRuleProcessingService layers,
    CategoryRowLayoutRuleProcessingService rows,
    TreeSpacingLayoutRuleProcessingService trees,
    SharedParentCentringLayoutRuleProcessingService shared) : ILayoutInitializationService
{
    public void Initialize(RenderModel model)
    {
        depth.ApplyRule(model);
        layers.ApplyRule(model);
        rows.ApplyRule(model);
        trees.ApplyRule(model);
        shared.ApplyRule(model);
    }
}
