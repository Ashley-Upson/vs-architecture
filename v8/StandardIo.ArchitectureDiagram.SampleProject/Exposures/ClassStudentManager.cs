// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Services.Orchestrations;

namespace StandardIo.ArchitectureDiagram.SampleProject.Exposures;
public sealed class ClassStudentManager : IClassStudentManager
{
    private readonly IClassStudentOrchestrationService classStudentOrchestrationService;
    internal ClassStudentManager(IClassStudentOrchestrationService classStudentOrchestrationService)
    {
        this.classStudentOrchestrationService = classStudentOrchestrationService;
    }

    public Task<ClassStudent> CreateClassStudentAsync(ClassStudent classStudent) =>
        classStudentOrchestrationService.CreateClassStudentAsync(classStudent: classStudent);

    public Task<ClassStudent> ReadClassStudentAsync(string classStudentId) =>
        classStudentOrchestrationService.ReadClassStudentAsync(classStudentId: classStudentId);

    public Task<ClassStudent> UpdateClassStudentAsync(ClassStudent updatedClassStudent) =>
        classStudentOrchestrationService.UpdateClassStudentAsync(updatedClassStudent: updatedClassStudent);

    public Task DeleteClassStudentAsync(string classStudentId) =>
        classStudentOrchestrationService.DeleteClassStudentAsync(classStudentId: classStudentId);
}