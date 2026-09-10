// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Services.Processings;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Orchestrations;

public sealed class ClassStudentOrchestrationService : IClassStudentOrchestrationService
{
    private readonly IClassStudentProcessingService classStudentProcessingService;
    private readonly IClassStudentEventProcessingService classStudentEventProcessingService;

    public ClassStudentOrchestrationService(
        IClassStudentProcessingService classStudentProcessingService,
        IClassStudentEventProcessingService classStudentEventProcessingService)
    {
        this.classStudentProcessingService = classStudentProcessingService;
        this.classStudentEventProcessingService = classStudentEventProcessingService;
    }

    public async Task<ClassStudent> CreateAsync(ClassStudent model)
    {
        ClassStudent result = classStudentProcessingService.Create(model: model);
        await classStudentEventProcessingService.RaiseCreatedAsync(model: result);
        return result;
    }

    public async Task<ClassStudent> ReadAsync(string id)
    {
        ClassStudent result = classStudentProcessingService.Read(id: id);
        await classStudentEventProcessingService.RaiseReadAsync(model: result);
        return result;
    }

    public async Task<ClassStudent> UpdateAsync(ClassStudent model)
    {
        ClassStudent result = classStudentProcessingService.Update(model: model);
        await classStudentEventProcessingService.RaiseUpdatedAsync(model: result);
        return result;
    }

    public async Task DeleteAsync(string id)
    {
        ClassStudent model = classStudentProcessingService.Read(id: id);
        classStudentProcessingService.Delete(id: id);
        await classStudentEventProcessingService.RaiseDeletedAsync(model: model);
    }
}
