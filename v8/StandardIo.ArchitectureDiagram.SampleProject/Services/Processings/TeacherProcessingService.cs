// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using StandardIo.ArchitectureDiagram.SampleProject.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Processings;

public sealed class TeacherProcessingService : ITeacherProcessingService
{
    private readonly ITeacherService teacherService;

    public TeacherProcessingService(ITeacherService teacherService)
    {
        this.teacherService = teacherService;
    }

    public Teacher Create(Teacher model) => teacherService.Create(model: model);

    public Teacher Read(string id) => teacherService.Read(id: id);

    public Teacher Update(Teacher model) => teacherService.Update(model: model);

    public void Delete(string id) => teacherService.Delete(id: id);
}
