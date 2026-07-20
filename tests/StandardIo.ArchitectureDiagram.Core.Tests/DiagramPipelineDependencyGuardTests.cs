using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class DiagramPipelineDependencyGuardTests
{
    [Fact]
    public void Analysis_domains_do_not_depend_on_each_other_or_drawio()
    {
        var architecture = Sources("Services", "Foundations", "Analyses");
        var dataModel = Sources("Services", "Foundations", "DataModels")
            .Where(path => !path.EndsWith("DrawioDataModelRenderer.cs", StringComparison.Ordinal) &&
                           !path.EndsWith("IDataModelRenderer.cs", StringComparison.Ordinal));

        Assert.All(architecture, path =>
        {
            var source = File.ReadAllText(path);
            Assert.DoesNotContain("Services.Foundations.DataModels", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Models.DataModels", source, StringComparison.Ordinal);
            Assert.DoesNotContain("System.Xml.Linq", source, StringComparison.Ordinal);
        });
        Assert.All(dataModel, path =>
        {
            var source = File.ReadAllText(path);
            Assert.DoesNotContain("Models.Architectures", source, StringComparison.Ordinal);
            Assert.DoesNotContain("IArchitectureAnalyser", source, StringComparison.Ordinal);
            Assert.DoesNotContain("RootDiscovery", source, StringComparison.Ordinal);
            Assert.DoesNotContain("System.Xml.Linq", source, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Page_renderers_and_composer_preserve_one_way_dependencies()
    {
        var dataRenderer = Source("Services", "Foundations", "DataModels", "DrawioDataModelRenderer.cs");
        var architectureRenderer = Source("Services", "Foundations", "Renderers", "DrawioArchitectureRenderer.cs");
        var composer = Source("Services", "Foundations", "Drawios", "DrawioDocumentComposer.cs");

        Assert.DoesNotContain("ArchitectureRenderer", File.ReadAllText(dataRenderer), StringComparison.Ordinal);
        Assert.DoesNotContain("DataModelRenderer", File.ReadAllText(architectureRenderer), StringComparison.Ordinal);
        var composerSource = File.ReadAllText(composer);
        Assert.DoesNotContain("Architectures", composerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DataModels", composerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Analysers_and_renderers_do_not_invoke_peer_pipeline_contracts()
    {
        var architectureAnalyser = File.ReadAllText(Source("Services", "Foundations", "Analyses", "RoslynDependencyAnalyzer.cs"));
        var dataAnalyser = File.ReadAllText(Source("Services", "Foundations", "DataModels", "RoslynDataModelAnalyser.cs"));
        var architectureRenderer = File.ReadAllText(Source("Services", "Foundations", "Renderers", "DrawioArchitectureRenderer.cs"));
        var dataRenderer = File.ReadAllText(Source("Services", "Foundations", "DataModels", "DrawioDataModelRenderer.cs"));

        Assert.DoesNotContain("IDataModelAnalyser", architectureAnalyser, StringComparison.Ordinal);
        Assert.DoesNotContain("IArchitectureAnalyser", dataAnalyser, StringComparison.Ordinal);
        Assert.DoesNotContain("IDataModelRenderer", architectureRenderer, StringComparison.Ordinal);
        Assert.DoesNotContain("IArchitectureRenderer", dataRenderer, StringComparison.Ordinal);
    }

    [Fact]
    public void Cli_has_no_concrete_exporter_or_duplicate_architecture_result_dependency()
    {
        var source = File.ReadAllText(Path.Combine(Root(), "src", "StandardIo.ArchitectureDiagram.Cli", "Program.cs"));

        Assert.DoesNotContain("DeterministicDrawioExporter", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IDeterministicDrawioExporter", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DrawioGenerationResult", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GenerateProjectRegion", source, StringComparison.Ordinal);
        Assert.Contains("IArchitectureGenerationService", source, StringComparison.Ordinal);
    }

    private static IEnumerable<string> Sources(params string[] segments) =>
        Directory.EnumerateFiles(Path.Combine(new[] { Root(), "src", "StandardIo.ArchitectureDiagram.Core" }.Concat(segments).ToArray()), "*.cs");

    private static string Source(params string[] segments) =>
        Path.Combine(new[] { Root(), "src", "StandardIo.ArchitectureDiagram.Core" }.Concat(segments).ToArray());

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "StandardIo.ArchitectureDiagram.sln")))
            directory = directory.Parent;
        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }
}
