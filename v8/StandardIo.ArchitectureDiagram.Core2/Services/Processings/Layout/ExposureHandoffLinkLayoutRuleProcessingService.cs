using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
internal sealed class ExposureHandoffLinkLayoutRuleProcessingService : ArchitectureLayoutRuleProcessingService
{
    protected override void ApplyArchitectureRule(RenderModel model)
    {
        if(model.DiagramType!=DiagramTypes.Architecture) return;
        LayerLinkClassification.Apply(model,edge=>
        {
            var value=LayerLinkClassification.Describe(model,edge);
            if(!edge.IsComposition && !edge.Inheritance && value.From!=RenderNodeCategory.Other && value.To==RenderNodeCategory.Exposure && !(value.From==value.To && value.SameProject))
                return edge with {IsLayerViolation=value.From!=RenderNodeCategory.Broker && value.A>=0 && value.A<value.Layers-1};
            return edge;
        });
    }
}
