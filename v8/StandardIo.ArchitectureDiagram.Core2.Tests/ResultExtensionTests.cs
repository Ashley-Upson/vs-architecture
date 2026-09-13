// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Linq;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core2.Exposures;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
using Xunit;
namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public sealed class ResultExtensionTests
{
    [Theory]
    [InlineData("broker.Read().Adapt();")]
    [InlineData("var value = broker.Read(); var alias = value; alias.Adapt();")]
    [InlineData("Fetch().Adapt().Adapt();")]
    [InlineData("Extensions.Adapt(broker.Read());")]
    public async Task ShouldAttachExtensionsOnReturnedValuesWithoutHidingDirectDependencies(string expression)
    {
        var model = await ConcreteCallChainTests.ExtractAsync("""
            public class Payload { public string Value { get; set; } }
            public class Broker { public Payload Read() => new(); }
            public static class Extensions { public static Payload Adapt(this Payload value) => value; }
            public static class Files { public static void Open() {} }
            public class Connection { public void Connect() {} }
            public class Consumer {
                readonly Broker broker;
                public Consumer(Broker broker) { this.broker = broker; }
                public void Run() { EXPRESSION Files.Open(); new Connection(); }
                private Payload Fetch() => broker.Read();
            }
            """.Replace("EXPRESSION", expression));
        var presented = TestServices.Get<IProjectModelPresentationService>().Prepare(model);
        Assert.DoesNotContain(presented.Model.Dependencies!, e => e.FromType == "Consumer" && e.ToType == "Extensions");
        Assert.DoesNotContain(presented.Model.Types!, t => t.Name == "Extensions");
        Assert.Contains("Extensions: Extensions", presented.Labels["Consumer"]);
        Assert.DoesNotContain("Adapt", presented.Labels["Consumer"]);
        var trees = TestServices.Get<ProjectModelSplitter>().Split(model);
        Assert.Contains(trees.SelectMany(t => t.Dependencies!), e => e.IsResultExtension);
        Assert.Contains(presented.Model.Dependencies!, e => e.FromType == "Consumer" && e.ToType == "Broker");
        Assert.Contains(presented.Model.Dependencies!, e => e.FromType == "Consumer" && e.ToType == "Files");
        Assert.Contains(presented.Model.Dependencies!, e => e.FromType == "Consumer" && e.ToType == "Connection");
        Assert.Contains(model.Dependencies!, e => e.ToType == "Extensions" && e.ToMethod == "Adapt");
    }
    [Theory]
    [InlineData("new Payload().Adapt();")]
    [InlineData("var value = broker.Read(); value = new Payload(); value.Adapt();")]
    [InlineData("broker.Read().Adapt(); new Payload().Adapt();")]
    public async Task ShouldKeepOrdinaryUsageEvenWhenTheSameMethodAlsoHasResultUsage(string expression)
    {
        var model = await ConcreteCallChainTests.ExtractAsync("""
            public class Payload { public string Value { get; set; } }
            public class Broker { public Payload Read() => new(); }
            public static class Extensions { public static Payload Adapt(this Payload value) => value; }
            public class Consumer {
                readonly Broker broker;
                public Consumer(Broker broker) { this.broker = broker; }
                public void Run() { EXPRESSION }
            }
            """.Replace("EXPRESSION", expression));
        var presented = TestServices.Get<IProjectModelPresentationService>().Prepare(model);
        Assert.Contains(presented.Model.Dependencies!, e => e.FromType == "Consumer" && e.ToType == "Extensions");
        Assert.Contains(presented.Model.Types!, t => t.Name == "Extensions");
    }
}
