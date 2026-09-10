// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Services.Processings;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Orchestrations;

public sealed class StudentOrchestrationService : IStudentOrchestrationService
{
    private readonly IStudentProcessingService studentProcessingService;
    private readonly IStudentEventProcessingService studentEventProcessingService;

    public StudentOrchestrationService(
        IStudentProcessingService studentProcessingService,
        IStudentEventProcessingService studentEventProcessingService)
    {
        this.studentProcessingService = studentProcessingService;
        this.studentEventProcessingService = studentEventProcessingService;
    }

    public async Task<Student> CreateAsync(Student model)
    {
        Student result = studentProcessingService.Create(model: model);
        await studentEventProcessingService.RaiseCreatedAsync(model: result);
        return result;
    }

    public async Task<Student> ReadAsync(string id)
    {
        Student result = studentProcessingService.Read(id: id);
        await studentEventProcessingService.RaiseReadAsync(model: result);
        return result;
    }

    public async Task<Student> UpdateAsync(Student model)
    {
        Student result = studentProcessingService.Update(model: model);
        await studentEventProcessingService.RaiseUpdatedAsync(model: result);
        return result;
    }

    public async Task DeleteAsync(string id)
    {
        Student model = studentProcessingService.Read(id: id);
        studentProcessingService.Delete(id: id);
        await studentEventProcessingService.RaiseDeletedAsync(model: model);
    }
}
