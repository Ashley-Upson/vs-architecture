using System.Threading;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Models.DataModels;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.DataModels;

public interface IDataModelRenderer<TPage>
{
    TPage Render(DataModelDiagram model, DataModelRenderSettings settings, CancellationToken cancellationToken = default);
}
