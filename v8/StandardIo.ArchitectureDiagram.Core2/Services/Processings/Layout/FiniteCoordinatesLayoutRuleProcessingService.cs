using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
internal sealed class FiniteCoordinatesLayoutRuleProcessingService : ILayoutRuleProcessingService
{
    public void ApplyRule(RenderModel model) { }
    public System.Collections.Generic.IEnumerable<string> GetViolations(RenderModel model) => LayoutConditions.FiniteCoordinates(model);
}
