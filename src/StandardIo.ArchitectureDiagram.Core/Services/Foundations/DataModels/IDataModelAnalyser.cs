using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using StandardIo.ArchitectureDiagram.Core.Models;
using StandardIo.ArchitectureDiagram.Core.Models.DataModels;

namespace StandardIo.ArchitectureDiagram.Core.Services.Foundations.DataModels;

public interface IDataModelAnalyser
{
    Task<DataModelDiagram> AnalyseAsync(
        IEnumerable<Project> selectedProjects,
        DataModelAnalysisSettings settings,
        CancellationToken cancellationToken = default);
}
