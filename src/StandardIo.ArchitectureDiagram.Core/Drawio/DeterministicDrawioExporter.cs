using System;
using StandardIo.ArchitectureDiagram.Core.Graph;
using StandardIo.ArchitectureDiagram.Core.Settings;

namespace StandardIo.ArchitectureDiagram.Core.Drawio;

public sealed partial class DeterministicDrawioExporter
{
    private const int TextWidth = 8;
    private const string ExposureTreeIdPrefix = "tree_";

    public string Export(DiagramModel diagram, DiagramSettings settings)
    {
        if (diagram is null)
        {
            throw new ArgumentNullException(nameof(diagram));
        }

        settings ??= DiagramSettings.CreateDefault();
        var graph = RenderGraph.From(diagram, settings);
        var layout = RenderLayout.Build(graph, settings);

        return new DiagramFileBuilder(settings).Build(layout);
    }
}
