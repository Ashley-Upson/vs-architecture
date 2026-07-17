using System.Xml.Linq;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Services.Foundations.Renderers;

var outputPath = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.GetFullPath(Path.Combine("artifacts", "node-duplication", "node-duplication-fixtures.drawio"));
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

var cases = new[]
{
    Fixture("Duplication enabled", SharedServiceModel(2), Settings(true)),
    Fixture("Duplication disabled", SharedServiceModel(3), Settings(false)),
    Fixture("Disabled with ILogger exception", LoggerModel(), Settings(false, "ILogger")),
    Fixture("Exposure-tree shared dependency", ExposureModel(), Settings(false)),
    Fixture("Multi-project external dependency", MultiProjectExternalModel(), Settings(false))
};
var mxfile = new XElement("mxfile",
    new XAttribute("host", "app.diagrams.net"),
    new XAttribute("modified", "2026-07-17T00:00:00.000Z"),
    new XAttribute("agent", "StandardIo.ArchitectureDiagram.NodeDuplicationFixtures"),
    new XAttribute("version", "26.0.0"));

for (var index = 0; index < cases.Length; index++)
{
    var (name, diagram) = cases[index];
    diagram.SetAttributeValue("id", $"node-duplication-{index + 1}");
    diagram.SetAttributeValue("name", name);
    mxfile.Add(diagram);
}

File.WriteAllText(outputPath, new XDocument(mxfile).ToString(SaveOptions.DisableFormatting));
Console.WriteLine(outputPath);

static (string Name, XElement Diagram) Fixture(string name, DiagramModel model, DiagramSettings settings)
{
    var rendered = XDocument.Parse(new DrawioDiagramRenderer().Render(model, settings));
    return (name, new XElement(rendered.Root!.Elements("diagram").First()));
}

static DiagramSettings Settings(bool allowDuplicates, params string[] exceptionPatterns)
{
    var settings = DiagramSettings.CreateDefault();
    settings.Layout.ExposureTreeLayoutThreshold = 1;
    settings.NodeDuplication.AllowDuplicateNodes = allowDuplicates;
    settings.NodeDuplication.DuplicationExceptionPatterns.AddRange(exceptionPatterns);
    return settings;
}

static DiagramModel SharedServiceModel(int parentCount)
{
    var parents = Enumerable.Range(1, parentCount)
        .Select(index => Node($"parent_{index}", "project", $"Parent{index}Controller"))
        .ToArray();
    return new DiagramModel(
        new[] { new ProjectContainer("project", "Shared Service", parents.Append(Node("shared", "project", "SharedProcessingService")).ToArray()) },
        Array.Empty<ExternalDependencyNode>(),
        parents.Select((parent, index) => new DependencyEdge($"edge_{index + 1}", parent.Id, "shared", "internal")).ToArray());
}

static DiagramModel LoggerModel() => ExternalModel(
    new[]
    {
        new ProjectContainer("project", "Logger Exception", new[]
        {
            Node("parent_1", "project", "FirstProcessingService"),
            Node("parent_2", "project", "SecondProcessingService")
        })
    });

static DiagramModel MultiProjectExternalModel() => ExternalModel(
    new[]
    {
        new ProjectContainer("project_a", "Project A", new[] { Node("parent_1", "project_a", "FirstProcessingService") }),
        new ProjectContainer("project_b", "Project B", new[] { Node("parent_2", "project_b", "SecondProcessingService") })
    });

static DiagramModel ExternalModel(IReadOnlyList<ProjectContainer> projects)
{
    var parents = projects.SelectMany(project => project.Types).ToArray();
    return new DiagramModel(
        projects,
        new[]
        {
            new ExternalDependencyNode(
                "logger",
                "ILogger",
                "Microsoft.Extensions.Logging",
                "logger-guid",
                "Microsoft.Extensions.Logging.ILogger",
                "[External]")
        },
        parents.Select((parent, index) => new DependencyEdge($"logger_edge_{index + 1}", parent.Id, "logger", "external")).ToArray());
}

static DiagramModel ExposureModel() => new(
    new[]
    {
        new ProjectContainer("project", "Exposure Tree", new[]
        {
            Node("first", "project", "FirstExposure"),
            Node("second", "project", "SecondExposure"),
            Node("first_processing", "project", "FirstProcessingService"),
            Node("second_processing", "project", "SecondProcessingService"),
            Node("shared", "project", "SharedBroker")
        })
    },
    Array.Empty<ExternalDependencyNode>(),
    new[]
    {
        new DependencyEdge("first_processing", "first", "first_processing", "internal"),
        new DependencyEdge("second_processing", "second", "second_processing", "internal"),
        new DependencyEdge("first_shared", "first_processing", "shared", "internal"),
        new DependencyEdge("second_shared", "second_processing", "shared", "internal")
    });

static TypeNode Node(string id, string projectId, string name) =>
    new(id, projectId, name, $"Fixture.{name}", "Class");
