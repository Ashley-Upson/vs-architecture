// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Processings;

public sealed class StudentEventProcessingService : IStudentEventProcessingService
{
    private readonly IStudentEventService studentEventService;

    public StudentEventProcessingService(IStudentEventService studentEventService)
    {
        this.studentEventService = studentEventService;
    }

    public ValueTask RaiseCreatedAsync(Student model) =>
        studentEventService.RaiseCreatedAsync(model: model);

    public ValueTask RaiseReadAsync(Student model) =>
        studentEventService.RaiseReadAsync(model: model);

    public ValueTask RaiseUpdatedAsync(Student model) =>
        studentEventService.RaiseUpdatedAsync(model: model);

    public ValueTask RaiseDeletedAsync(Student model) =>
        studentEventService.RaiseDeletedAsync(model: model);
}
