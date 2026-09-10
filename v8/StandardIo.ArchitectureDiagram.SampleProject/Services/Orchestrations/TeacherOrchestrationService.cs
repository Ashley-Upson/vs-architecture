// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Services.Processings;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Orchestrations;

public sealed class TeacherOrchestrationService : ITeacherOrchestrationService
{
    private readonly ITeacherProcessingService teacherProcessingService;
    private readonly ITeacherEventProcessingService teacherEventProcessingService;

    public TeacherOrchestrationService(
        ITeacherProcessingService teacherProcessingService,
        ITeacherEventProcessingService teacherEventProcessingService)
    {
        this.teacherProcessingService = teacherProcessingService;
        this.teacherEventProcessingService = teacherEventProcessingService;
    }

    public async Task<Teacher> CreateAsync(Teacher model)
    {
        Teacher result = teacherProcessingService.Create(model: model);
        await teacherEventProcessingService.RaiseCreatedAsync(model: result);
        return result;
    }

    public async Task<Teacher> ReadAsync(string id)
    {
        Teacher result = teacherProcessingService.Read(id: id);
        await teacherEventProcessingService.RaiseReadAsync(model: result);
        return result;
    }

    public async Task<Teacher> UpdateAsync(Teacher model)
    {
        Teacher result = teacherProcessingService.Update(model: model);
        await teacherEventProcessingService.RaiseUpdatedAsync(model: result);
        return result;
    }

    public async Task DeleteAsync(string id)
    {
        Teacher model = teacherProcessingService.Read(id: id);
        teacherProcessingService.Delete(id: id);
        await teacherEventProcessingService.RaiseDeletedAsync(model: model);
    }
}
