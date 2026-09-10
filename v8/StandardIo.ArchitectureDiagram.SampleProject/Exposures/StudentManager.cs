// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Services.Orchestrations;

namespace StandardIo.ArchitectureDiagram.SampleProject.Exposures;

public sealed class StudentManager : IStudentManager
{
    private readonly IStudentOrchestrationService studentOrchestrationService;

    public StudentManager(IStudentOrchestrationService studentOrchestrationService)
    {
        this.studentOrchestrationService = studentOrchestrationService;
    }

    public Task<Student> CreateAsync(Student model) => studentOrchestrationService.CreateAsync(model: model);

    public Task<Student> ReadAsync(string id) => studentOrchestrationService.ReadAsync(id: id);

    public Task<Student> UpdateAsync(Student model) => studentOrchestrationService.UpdateAsync(model: model);

    public Task DeleteAsync(string id) => studentOrchestrationService.DeleteAsync(id: id);
}
