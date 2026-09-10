// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Services.Processings;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Orchestrations;
internal sealed class ClassOrchestrationService : IClassOrchestrationService
{
    private readonly IClassProcessingService classProcessingService;
    private readonly IClassEventProcessingService classEventProcessingService;
    public ClassOrchestrationService(IClassProcessingService classProcessingService, IClassEventProcessingService classEventProcessingService)
    {
        this.classProcessingService = classProcessingService;
        this.classEventProcessingService = classEventProcessingService;
    }

    public async Task<Class> CreateClassAsync(Class @class)
    {
        Class result = classProcessingService.CreateClass(@class: @class);
        await classEventProcessingService.RaiseClassCreatedAsync(@class: result);
        return result;
    }

    public async Task<Class> ReadClassAsync(string classId)
    {
        Class result = classProcessingService.ReadClass(classId: classId);
        await classEventProcessingService.RaiseClassReadAsync(@class: result);
        return result;
    }

    public async Task<Class> UpdateClassAsync(Class updatedClass)
    {
        Class result = classProcessingService.UpdateClass(updatedClass: updatedClass);
        await classEventProcessingService.RaiseClassUpdatedAsync(@class: result);
        return result;
    }

    public async Task DeleteClassAsync(string classId)
    {
        Class model = classProcessingService.ReadClass(classId: classId);
        classProcessingService.DeleteClass(classId: classId);
        await classEventProcessingService.RaiseClassDeletedAsync(@class: model);
    }
}