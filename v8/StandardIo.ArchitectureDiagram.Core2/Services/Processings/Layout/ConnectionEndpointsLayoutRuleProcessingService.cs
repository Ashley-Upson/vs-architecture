using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
internal sealed class ConnectionEndpointsLayoutRuleProcessingService : ILayoutRuleProcessingService
{
    public void ApplyRule(RenderModel model) { }
    public IEnumerable<string> GetViolations(RenderModel model)
    {
        var ids=model.Projects.SelectMany(p=>p.Nodes).Select(n=>n.Id).ToHashSet();
        return model.Projects.SelectMany(p=>p.Connections).Concat(model.CrossProjectConnections)
            .Where(e=>!ids.Contains(e.SourceId)||!ids.Contains(e.TargetId)).Select(e=>"Missing connection endpoint: "+e.Id);
    }
}
