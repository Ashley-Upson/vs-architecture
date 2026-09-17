using System;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
internal static class ProjectLayout
{
    internal static void Apply(RenderModel model,Action<RenderModel> adjustment)
    {
        if(model.CrossProjectConnections.Length==0) return;
        var graph=LayoutGraph.ProjectGraph(model);
        var containers=new RenderModel(graph.Width,graph.Height,[graph]) { Configuration=model.Configuration, IsProjectGraph=true, LayoutInitialized=true };
        adjustment(containers);
        for(int i=0;i<model.Projects.Length;i++)
        {
            var node=graph.Nodes.Single(n=>n.Id==model.Projects[i].Id);
            model.Projects[i]=model.Projects[i] with {X=node.X,Y=node.Y};
        }
    }
}
