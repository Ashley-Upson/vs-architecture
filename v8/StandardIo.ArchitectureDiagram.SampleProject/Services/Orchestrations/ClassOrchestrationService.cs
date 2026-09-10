// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Services.Processings;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Orchestrations;

public sealed class ClassOrchestrationService : IClassOrchestrationService
{
    private readonly IClassProcessingService classProcessingService;
    private readonly IClassEventProcessingService classEventProcessingService;

    public ClassOrchestrationService(
        IClassProcessingService classProcessingService,
        IClassEventProcessingService classEventProcessingService)
    {
        this.classProcessingService = classProcessingService;
        this.classEventProcessingService = classEventProcessingService;
    }

    public async Task<Class> CreateAsync(Class model)
    {
        Class result = classProcessingService.Create(model: model);
        await classEventProcessingService.RaiseCreatedAsync(model: result);
        return result;
    }

    public async Task<Class> ReadAsync(string id)
    {
        Class result = classProcessingService.Read(id: id);
        await classEventProcessingService.RaiseReadAsync(model: result);
        return result;
    }

    public async Task<Class> UpdateAsync(Class model)
    {
        Class result = classProcessingService.Update(model: model);
        await classEventProcessingService.RaiseUpdatedAsync(model: result);
        return result;
    }

    public async Task DeleteAsync(string id)
    {
        Class model = classProcessingService.Read(id: id);
        classProcessingService.Delete(id: id);
        await classEventProcessingService.RaiseDeletedAsync(model: model);
    }
}
