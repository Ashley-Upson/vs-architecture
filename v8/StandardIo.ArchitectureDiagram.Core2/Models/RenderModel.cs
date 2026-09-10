// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
namespace StandardIo.ArchitectureDiagram.Core2.Models;
public sealed record RenderModel(double Width, double Height, RenderProject[] Projects)
{
    public double Width { get; set; } = Width;
    public double Height { get; set; } = Height;
    public RenderConnection[] CrossProjectConnections { get; set; } = [];
    internal int LayoutIterations { get; set; }
    internal bool ProjectLayoutInitialized { get; set; }
    internal bool LayoutInitialized { get; set; }
    public RenderConfiguration Configuration { get; set; } = new();
    public ProjectModel[] ProjectModels { get; set; } = [];
    public DiagramTypes DiagramType { get; set; } = DiagramTypes.Architecture;
    public RenderProject[] Projects { get; set; } = Projects;
    internal bool IsProjectGraph { get; set; }
    public RenderModel(ProjectModel[] projectModels, RenderConfiguration? configuration = null, DiagramTypes diagramType = DiagramTypes.Architecture)
        : this(0, 0, [])
    {
        ProjectModels = projectModels;
        Configuration = configuration ?? new();
        DiagramType = diagramType;
    }
}
