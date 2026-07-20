using StandardIo.ArchitectureDiagram.Core.Models.Generation;

namespace StandardIo.ArchitectureDiagram.Core.Services.Processings.Drawios;

public interface IArchitectureGeometryAnalyser
{
    ArchitectureGeometryAnalysis Analyse(TypedArchitectureGenerationResult generation);
    string ToJson(ArchitectureGeometryAnalysis analysis);
    string ToMarkdown(ArchitectureGeometryAnalysis analysis);
}
