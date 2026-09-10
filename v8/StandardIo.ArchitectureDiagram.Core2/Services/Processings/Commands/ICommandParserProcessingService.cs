using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Commands;
internal interface ICommandParserProcessingService { DiagramRenderRequest Parse(string[] command); }
