using System.Collections.Generic;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
internal interface IHtmlDocumentService { byte[] Render(IReadOnlyList<ProjectModelDrawing> drawings); }
