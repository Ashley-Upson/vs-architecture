using System.Collections.Generic;
using StandardIo.ArchitectureDiagram.Core.Models.Drawios;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.Drawios;

public interface IDrawioDocumentComposer
{
    DrawioDocument Compose(IReadOnlyList<DrawioPage> pages, DrawioDocumentSettings settings);
}
