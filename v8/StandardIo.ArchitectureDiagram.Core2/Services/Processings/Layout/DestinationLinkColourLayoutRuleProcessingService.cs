using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
internal sealed class DestinationLinkColourLayoutRuleProcessingService : ArchitectureLayoutRuleProcessingService
{
    protected override void ApplyArchitectureRule(RenderModel model)
    {
        var nodes=model.Projects.SelectMany(p=>p.Nodes).ToDictionary(n=>n.Id);
        LayerLinkClassification.Apply(model,edge=>edge.IsLayerViolation?edge:
            edge with {Stroke=model.Configuration.ColourLines?nodes[edge.TargetId].Fill:"#d1d5db"});
    }
}
