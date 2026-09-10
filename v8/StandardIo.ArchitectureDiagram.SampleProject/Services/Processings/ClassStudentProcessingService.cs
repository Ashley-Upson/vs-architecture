// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using StandardIo.ArchitectureDiagram.SampleProject.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Processings;

public sealed class ClassStudentProcessingService : IClassStudentProcessingService
{
    private readonly IClassStudentService classStudentService;

    public ClassStudentProcessingService(IClassStudentService classStudentService)
    {
        this.classStudentService = classStudentService;
    }

    public ClassStudent Create(ClassStudent model) => classStudentService.Create(model: model);

    public ClassStudent Read(string id) => classStudentService.Read(id: id);

    public ClassStudent Update(ClassStudent model) => classStudentService.Update(model: model);

    public void Delete(string id) => classStudentService.Delete(id: id);
}
