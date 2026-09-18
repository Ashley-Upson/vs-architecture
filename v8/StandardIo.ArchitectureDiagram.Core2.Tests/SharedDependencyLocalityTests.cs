using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
using Xunit;
namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public sealed class SharedDependencyLocalityTests
{
    [Fact]
    public void ShouldMoveBrokerTowardsSharedDependenciesBeyondItsSiblingBranch()
    {
        // Given: the unchanged PageRenderCacheController tree captured from the diagram.
        (string Name,double X,double Y)[] positions = [
            ("LoggingBroker", 175.0, 1180.0),
            ("AuthorizationService", 3010.0, 860.0),
            ("AuthorizationProcessingService", 3010.0, 700.0),
            ("AuthorizationBroker", 3010.0, 1180.0),
            ("CommonObjectLatestCacheProcessingService", 1187.4999999999998, 700.0),
            ("CommonObjectLatestCacheService", 1187.4999999999998, 860.0),
            ("CacheBroker", 984.9999999999998, 1180.0),
            ("JsonBroker", 1794.9999999999998, 1180.0),
            ("CommonObjectBroker", 1254.9999999999998, 1180.0),
            ("MemoryCacheDependency", 1119.9999999999998, 1500.0),
            ("PageRenderCacheController", 1136.875, 60.0),
            ("PageRenderCacheManager", 2098.75, 220.0),
            ("PageRenderCacheAggregationService", 2098.75, 380.0),
            ("PageRenderCacheOrchestrationService", 2098.75, 540.0),
            ("PageRenderCacheProcessingService", 2740.0, 700.0),
            ("PageRenderCacheService", 2740.0, 860.0),
            ("PageRenderCacheBroker", 2740.0, 1180.0),
            ("IDbContextTransaction", 2740.0, 1340.0),
            ("CacheExtensions", 849.9999999999998, 1340.0),
            ("MemoryCache", 1119.9999999999998, 1660.0),
            ("ILogger", 40.0, 1340.0),
            ("LoggerExtensions", 310.0, 1340.0),
            ("Object", 580.0, 1020.0),
            ("JsonDocument", 1119.9999999999998, 1340.0),
            ("JsonSerializer", 1389.9999999999998, 1340.0),
            ("JsonSerializerOptions", 1659.9999999999998, 1340.0),
            ("JsonNode", 1929.9999999999998, 1340.0),
            ("JsonObject", 2200.0, 1340.0),
            ("JsonValue", 2470.0, 1340.0),
            ("ICoreContextFactory", 3280.0, 1340.0),
            ("CoreDataContext", 3010.0, 1340.0)];
        (int Source,int Target)[] links = [(3, 29),(0, 20),(0, 21),(1, 3),(2, 1),(6, 18),(6, 9),(7, 23),(7, 24),(7, 25),(7, 26),(7, 27),(7, 28),(8, 30),(8, 29),(9, 19),(5, 22),(5, 6),(5, 7),(5, 8),(4, 5),(16, 17),(16, 30),(16, 29),(10, 0),(10, 11),(11, 12),(12, 13),(15, 16),(13, 4),(13, 2),(13, 14),(14, 15)];
        var nodes=positions.Select((p,i)=>new RenderNode(i.ToString(),p.Name,p.Name,"blue",p.X,p.Y,210,60,[])).ToArray();
        var edges=links.Select((p,i)=>new RenderConnection("e"+i,p.Source.ToString(),p.Target.ToString(),positions[p.Source].Name,positions[p.Target].Name,false,[])).ToArray();
        var project=new RenderProject("p","P",0,0,4000,2000,nodes,edges);
        var model=new RenderModel(4000,2000,[project]);
        // When: the same locality pass used by the complete architecture pipeline runs.
        new NodeLocalityLayoutRuleProcessingService().ApplyRule(model);
        // Then: the database broker sits beyond the JSON branch, near its shared targets.
        Assert.True(project.Nodes.Single(n=>n.TypeName=="CommonObjectBroker").X > project.Nodes.Single(n=>n.TypeName=="JsonBroker").X);
    }
}
