// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Processings;
internal sealed class StudentEventProcessingService : IStudentEventProcessingService
{
    private readonly IStudentEventService studentEventService;
    public StudentEventProcessingService(IStudentEventService studentEventService)
    {
        this.studentEventService = studentEventService;
    }

    public ValueTask RaiseStudentCreatedAsync(Student student) =>
        studentEventService.RaiseStudentCreatedAsync(student: student);

    public ValueTask RaiseStudentReadAsync(Student student) =>
        studentEventService.RaiseStudentReadAsync(student: student);

    public ValueTask RaiseStudentUpdatedAsync(Student student) =>
        studentEventService.RaiseStudentUpdatedAsync(student: student);

    public ValueTask RaiseStudentDeletedAsync(Student student) =>
        studentEventService.RaiseStudentDeletedAsync(student: student);
}