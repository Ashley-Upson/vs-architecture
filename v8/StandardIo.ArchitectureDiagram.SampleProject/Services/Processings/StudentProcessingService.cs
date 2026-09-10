// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using StandardIo.ArchitectureDiagram.SampleProject.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Processings;

public sealed class StudentProcessingService : IStudentProcessingService
{
    private readonly IStudentService studentService;

    public StudentProcessingService(IStudentService studentService)
    {
        this.studentService = studentService;
    }

    public Student Create(Student model) => studentService.Create(model: model);

    public Student Read(string id) => studentService.Read(id: id);

    public Student Update(Student model) => studentService.Update(model: model);

    public void Delete(string id) => studentService.Delete(id: id);
}
