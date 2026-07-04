namespace StandardIo.ArchitectureDiagram.Core.Models;

public sealed class DiagramPathGenerationResult
{
    public DiagramPathGenerationResult(
        string targetName,
        string outputPath,
        string rendererId,
        string content)
    {
        TargetName = targetName;
        OutputPath = outputPath;
        RendererId = rendererId;
        Content = content;
    }

    public string TargetName { get; }

    public string OutputPath { get; }

    public string RendererId { get; }

    public string Content { get; }
}
