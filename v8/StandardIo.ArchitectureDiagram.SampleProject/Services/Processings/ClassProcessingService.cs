// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Processings;
internal sealed class ClassProcessingService : IClassProcessingService
{
    private readonly IClassService classService;
    public ClassProcessingService(IClassService classService)
    {
        this.classService = classService;
    }

    public Class CreateClass(Class @class) =>
        classService.CreateClass(@class: @class);

    public Class ReadClass(string classId) =>
        classService.ReadClass(classId: classId);

    public Class UpdateClass(Class updatedClass) =>
        classService.UpdateClass(updatedClass: updatedClass);

    public void DeleteClass(string classId) =>
        classService.DeleteClass(classId: classId);
}