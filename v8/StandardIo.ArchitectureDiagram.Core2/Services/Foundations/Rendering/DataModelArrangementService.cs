using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
internal interface IDataModelArrangementService
{
    RenderNode[] Arrange(RenderNode[] nodes, ContextualLink[] links, DataModelRenderConfiguration configuration);
}
internal sealed class DataModelArrangementService : IDataModelArrangementService
{
    public RenderNode[] Arrange(RenderNode[] nodes,ContextualLink[] links,DataModelRenderConfiguration configuration)
    {
        var byName=nodes.ToDictionary(n=>n.TypeName);
        var local=links.Where(l=>l.From!=l.To&&byName.ContainsKey(l.From)&&byName.ContainsKey(l.To)).DistinctBy(l=>(l.From,l.To)).ToArray();
        var neighbours=local.SelectMany(l=>new[]{(From:l.From,To:l.To),(From:l.To,To:l.From)}).ToLookup(l=>l.From,l=>l.To);
        var incoming=local.ToLookup(l=>l.To);var outgoing=local.ToLookup(l=>l.From);
        var referenced=links.SelectMany(l=>new[]{l.From,l.To}).ToHashSet();
        var visited=new HashSet<string>();var clusters=new List<RenderNode[]>();
        RenderNode Move(RenderNode n,double x,double y)=>n with {X=x,Y=y,TextLines=n.TextLines.Select(t=>t with {X=t.X+x-n.X,Y=t.Y+y-n.Y}).ToArray()};
        foreach(var seed in nodes.Where(n=>referenced.Contains(n.TypeName)))
        {
            if(visited.Contains(seed.TypeName))continue;
            var component=new List<string>();var pending=new Queue<string>();pending.Enqueue(seed.TypeName);
            while(pending.TryDequeue(out var name))
            {
                if(!visited.Add(name))continue;component.Add(name);
                foreach(var neighbour in neighbours[name])pending.Enqueue(neighbour);
            }
            string root=component.OrderBy(n=>incoming[n].Any()).ThenByDescending(n=>outgoing[n].Count()).ThenBy(n=>n,StringComparer.Ordinal).First();
            var cells=new Dictionary<string,(int X,int Y)>{{root,(0,0)}};var occupied=new HashSet<(int,int)>{(0,0)};
            pending.Enqueue(root);
            (int X,int Y)[] directions=[(1,0),(0,1),(-1,0),(0,-1),(1,1),(-1,1),(-1,-1),(1,-1)];
            while(pending.TryDequeue(out var parent))
            foreach(string child in neighbours[parent].Distinct().OrderByDescending(n=>neighbours[n].Count()).ThenBy(n=>n,StringComparer.Ordinal))
            {
                if(cells.ContainsKey(child))continue;
                var origin=cells[parent];int radius=1; (int X,int Y) cell;
                while(true)
                {
                    var free=directions.Select(d=>(X:origin.X+d.X*radius,Y:origin.Y+d.Y*radius)).Where(p=>!occupied.Contains(p)).ToArray();
                    if(free.Length>0){cell=free.OrderBy(p=>neighbours[child].Where(cells.ContainsKey).Sum(n=>Math.Pow(p.X-cells[n].X,2)+Math.Pow(p.Y-cells[n].Y,2))).First();break;}radius++;
                }
                cells[child]=cell;occupied.Add(cell);pending.Enqueue(child);
            }
            // Refine the initial placement against the whole relationship graph,
            // not only the first parent that happened to discover each entity.
            var relationships=local.Where(l=>cells.ContainsKey(l.From)&&cells.ContainsKey(l.To)).DistinctBy(l=>string.CompareOrdinal(l.From,l.To)<0?(l.From,l.To):(l.To,l.From)).ToArray();
            double Turn((int X,int Y) a,(int X,int Y) b,(int X,int Y) c)=>(b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X);
            double Score()
            {
                double score=relationships.Sum(l=>Math.Sqrt(Math.Pow(cells[l.From].X-cells[l.To].X,2)+Math.Pow(cells[l.From].Y-cells[l.To].Y,2)));
                for(int i=0;i<relationships.Length;i++)for(int j=i+1;j<relationships.Length;j++)
                {
                    var a=cells[relationships[i].From];var b=cells[relationships[i].To];var c=cells[relationships[j].From];var d=cells[relationships[j].To];
                    if(Turn(a,b,c)*Turn(a,b,d)<0&&Turn(c,d,a)*Turn(c,d,b)<0)score+=8;
                }
                return score;
            }
            var movable=cells.Keys.Where(n=>n!=root).ToArray();double best=Score();
            for(int pass=0;pass<8;pass++)
            {
                bool changed=false;
                for(int i=0;i<movable.Length;i++)for(int j=i+1;j<movable.Length;j++)
                {
                    string a=movable[i],b=movable[j];(cells[a],cells[b])=(cells[b],cells[a]);double score=Score();
                    if(score<best-0.001){best=score;changed=true;}else (cells[a],cells[b])=(cells[b],cells[a]);
                }
                if(!changed)break;
            }
            var rowTops=new Dictionary<int,double>();double top=0;
            foreach(var row in cells.GroupBy(p=>p.Value.Y).OrderBy(g=>g.Key))
            {rowTops[row.Key]=top;top+=row.Max(p=>byName[p.Key].Height)+configuration.RowSpacing;}
            int minX=cells.Min(p=>p.Value.X);
            clusters.Add(cells.Select(p=>Move(byName[p.Key],(p.Value.X-minX)*(configuration.NodeWidth+configuration.NodeSpacing),rowTops[p.Value.Y])).ToArray());
        }
        var result=new List<RenderNode>();
        double gap=configuration.NodeSpacing*2;
        double target=Math.Max(clusters.Select(c=>c.Max(n=>n.X+n.Width)).DefaultIfEmpty(0).Max(),Math.Sqrt(clusters.Sum(c=>(c.Max(n=>n.X+n.Width)+gap)*(c.Max(n=>n.Y+n.Height)+gap))*1.5));
        double left=40,shelf=60,shelfHeight=0;
        foreach(var cluster in clusters.OrderByDescending(c=>c.Length))
        {
            double width=cluster.Max(n=>n.X+n.Width),height=cluster.Max(n=>n.Y+n.Height);
            if(left>40&&left+width>target+40){left=40;shelf+=shelfHeight+configuration.RowSpacing*2;shelfHeight=0;}
            result.AddRange(cluster.Select(n=>Move(n,n.X+left,n.Y+shelf)));
            left+=width+gap;shelfHeight=Math.Max(shelfHeight,height);
        }
        // Unattached types occupy their own area below the connected clusters.
        var isolated=nodes.Where(n=>!referenced.Contains(n.TypeName)).ToArray();
        if(isolated.Length>0)
        {
            double top=result.Count==0?60:result.Max(n=>n.Y+n.Height)+configuration.RowSpacing*2;
            int columns=Math.Max(4,(int)Math.Ceiling(Math.Sqrt(isolated.Length*(isolated.Average(n=>n.Height)+configuration.RowSpacing)*1.5/(configuration.NodeWidth+configuration.NodeSpacing))));
            foreach(var row in isolated.Chunk(columns))
            {
                for(int i=0;i<row.Length;i++)result.Add(Move(row[i],40+i*(configuration.NodeWidth+configuration.NodeSpacing),top));
                top+=row.Max(n=>n.Height)+configuration.RowSpacing;
            }
        }
        return result.ToArray();
    }
}
