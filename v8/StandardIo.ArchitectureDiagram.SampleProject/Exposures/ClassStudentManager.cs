// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Services.Orchestrations;

namespace StandardIo.ArchitectureDiagram.SampleProject.Exposures;

public sealed class ClassStudentManager : IClassStudentManager
{
    private readonly IClassStudentOrchestrationService classStudentOrchestrationService;

    public ClassStudentManager(IClassStudentOrchestrationService classStudentOrchestrationService)
    {
        this.classStudentOrchestrationService = classStudentOrchestrationService;
    }

    public Task<ClassStudent> CreateAsync(ClassStudent model) => classStudentOrchestrationService.CreateAsync(model: model);

    public Task<ClassStudent> ReadAsync(string id) => classStudentOrchestrationService.ReadAsync(id: id);

    public Task<ClassStudent> UpdateAsync(ClassStudent model) => classStudentOrchestrationService.UpdateAsync(model: model);

    public Task DeleteAsync(string id) => classStudentOrchestrationService.DeleteAsync(id: id);
}
