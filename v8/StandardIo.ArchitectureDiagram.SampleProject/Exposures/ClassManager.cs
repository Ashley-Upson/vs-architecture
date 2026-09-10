// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Services.Orchestrations;

namespace StandardIo.ArchitectureDiagram.SampleProject.Exposures;

public sealed class ClassManager : IClassManager
{
    private readonly IClassOrchestrationService classOrchestrationService;

    public ClassManager(IClassOrchestrationService classOrchestrationService)
    {
        this.classOrchestrationService = classOrchestrationService;
    }

    public Task<Class> CreateAsync(Class model) => classOrchestrationService.CreateAsync(model: model);

    public Task<Class> ReadAsync(string id) => classOrchestrationService.ReadAsync(id: id);

    public Task<Class> UpdateAsync(Class model) => classOrchestrationService.UpdateAsync(model: model);

    public Task DeleteAsync(string id) => classOrchestrationService.DeleteAsync(id: id);
}
