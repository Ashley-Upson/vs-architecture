using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
internal sealed class SameLayerLinkLayoutRuleProcessingService : ArchitectureLayoutRuleProcessingService
{
    protected override void ApplyArchitectureRule(RenderModel model)
    {
        if(model.DiagramType!=DiagramTypes.Architecture) return;
        LayerLinkClassification.Apply(model,edge=>
        {
            var value=LayerLinkClassification.Describe(model,edge);
            if(!edge.IsComposition && !edge.Inheritance && value.From==value.To && value.SameProject)
                return edge with {IsLayerViolation=false};
            return edge;
        });
    }
}
