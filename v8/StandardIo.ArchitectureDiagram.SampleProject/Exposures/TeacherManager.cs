// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Services.Orchestrations;

namespace StandardIo.ArchitectureDiagram.SampleProject.Exposures;

public sealed class TeacherManager : ITeacherManager
{
    private readonly ITeacherOrchestrationService teacherOrchestrationService;

    public TeacherManager(ITeacherOrchestrationService teacherOrchestrationService)
    {
        this.teacherOrchestrationService = teacherOrchestrationService;
    }

    public Task<Teacher> CreateAsync(Teacher model) => teacherOrchestrationService.CreateAsync(model: model);

    public Task<Teacher> ReadAsync(string id) => teacherOrchestrationService.ReadAsync(id: id);

    public Task<Teacher> UpdateAsync(Teacher model) => teacherOrchestrationService.UpdateAsync(model: model);

    public Task DeleteAsync(string id) => teacherOrchestrationService.DeleteAsync(id: id);
}
