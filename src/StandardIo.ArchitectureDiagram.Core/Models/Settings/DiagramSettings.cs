using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Models;

public sealed class DiagramSettings
{
    public int Version { get; set; } = 1;
    public CanvasSettings Canvas { get; set; } = new();
    public LayoutSettings Layout { get; set; } = new();
    public List<string> ExcludedNamespaces { get; set; } = new();
    public List<string> ExcludedNames { get; set; } = new();
    public List<StyleRule> StyleRules { get; set; } = new();
    public List<StyleOverride> Overrides { get; set; } = new();
    public string OutputRenderer { get; set; } = "drawio";
    public bool ShowProjectContainers { get; set; } = true;
    public string ExternalDependencyTag { get; set; } = "[External]";
    public NodeStyle ProjectContainerStyle { get; set; } = NodeStyle.ProjectContainer();
    public NodeStyle ExternalDependencyStyle { get; set; } = NodeStyle.External();
    public ConnectorStyle Connector { get; set; } = new();
    public NodeDuplicationSettings NodeDuplication { get; set; } = new();

    public static DiagramSettings CreateDefault()
    {
        return DiagramSettingsFactory.CreateDefault();
    }
}
