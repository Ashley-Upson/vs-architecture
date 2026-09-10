// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Services.Processings;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Orchestrations;
internal sealed class ClassStudentOrchestrationService : IClassStudentOrchestrationService
{
    private readonly IClassStudentProcessingService classStudentProcessingService;
    private readonly IClassStudentEventProcessingService classStudentEventProcessingService;
    public ClassStudentOrchestrationService(IClassStudentProcessingService classStudentProcessingService, IClassStudentEventProcessingService classStudentEventProcessingService)
    {
        this.classStudentProcessingService = classStudentProcessingService;
        this.classStudentEventProcessingService = classStudentEventProcessingService;
    }

    public async Task<ClassStudent> CreateClassStudentAsync(ClassStudent classStudent)
    {
        ClassStudent result = classStudentProcessingService.CreateClassStudent(classStudent: classStudent);
        await classStudentEventProcessingService.RaiseClassStudentCreatedAsync(classStudent: result);
        return result;
    }

    public async Task<ClassStudent> ReadClassStudentAsync(string classStudentId)
    {
        ClassStudent result = classStudentProcessingService.ReadClassStudent(classStudentId: classStudentId);
        await classStudentEventProcessingService.RaiseClassStudentReadAsync(classStudent: result);
        return result;
    }

    public async Task<ClassStudent> UpdateClassStudentAsync(ClassStudent updatedClassStudent)
    {
        ClassStudent result = classStudentProcessingService.UpdateClassStudent(updatedClassStudent: updatedClassStudent);
        await classStudentEventProcessingService.RaiseClassStudentUpdatedAsync(classStudent: result);
        return result;
    }

    public async Task DeleteClassStudentAsync(string classStudentId)
    {
        ClassStudent model = classStudentProcessingService.ReadClassStudent(classStudentId: classStudentId);
        classStudentProcessingService.DeleteClassStudent(classStudentId: classStudentId);
        await classStudentEventProcessingService.RaiseClassStudentDeletedAsync(classStudent: model);
    }
}