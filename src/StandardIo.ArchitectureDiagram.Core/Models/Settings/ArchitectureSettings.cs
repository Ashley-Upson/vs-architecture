using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.Core.Models;

public sealed class ArchitectureAnalysisSettings
{
    public List<string> ExcludedNamespaces { get; set; } = new();
    public List<string> ExcludedNames { get; set; } = new();
    public string RootDiscoveryPatternsText { get; set; } = string.Empty;
    public string ExternalDependencyTag { get; set; } = "[External]";
}

public sealed class ArchitectureRenderSettings
{
    public CanvasSettings Canvas { get; set; } = new();
    public LayoutSettings Layout { get; set; } = new();
    public List<StyleRule> StyleRules { get; set; } = new();
    public List<StyleOverride> Overrides { get; set; } = new();
    public bool ShowProjectContainers { get; set; } = true;
    public NodeStyle ProjectContainerStyle { get; set; } = NodeStyle.ProjectContainer();
    public NodeStyle ExternalDependencyStyle { get; set; } = NodeStyle.External();
    public ConnectorStyle Connector { get; set; } = new();
    public NodeDuplicationSettings NodeDuplication { get; set; } = new();
}
