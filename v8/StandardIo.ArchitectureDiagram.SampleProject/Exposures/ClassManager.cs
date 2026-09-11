// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Services.Orchestrations;

namespace StandardIo.ArchitectureDiagram.SampleProject.Exposures;
public sealed class ClassManager : IClassManager
{
    private readonly IClassOrchestrationService classOrchestrationService;
    internal ClassManager(IClassOrchestrationService classOrchestrationService)
    {
        this.classOrchestrationService = classOrchestrationService;
    }

    public Task<Class> CreateClassAsync(Class @class) =>
        classOrchestrationService.CreateClassAsync(@class: @class);

    public Task<Class> ReadClassAsync(string classId) =>
        classOrchestrationService.ReadClassAsync(classId: classId);

    public Task<Class> UpdateClassAsync(Class updatedClass) =>
        classOrchestrationService.UpdateClassAsync(updatedClass: updatedClass);

    public Task DeleteClassAsync(string classId) =>
        classOrchestrationService.DeleteClassAsync(classId: classId);
}