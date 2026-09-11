// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Processings;
internal sealed class ClassStudentProcessingService : IClassStudentProcessingService
{
    private readonly IClassStudentService classStudentService;
    public ClassStudentProcessingService(IClassStudentService classStudentService)
    {
        this.classStudentService = classStudentService;
    }

    public ClassStudent CreateClassStudent(ClassStudent classStudent) =>
        classStudentService.CreateClassStudent(classStudent: classStudent);

    public ClassStudent ReadClassStudent(string classStudentId) =>
        classStudentService.ReadClassStudent(classStudentId: classStudentId);

    public ClassStudent UpdateClassStudent(ClassStudent updatedClassStudent) =>
        classStudentService.UpdateClassStudent(updatedClassStudent: updatedClassStudent);

    public void DeleteClassStudent(string classStudentId) =>
        classStudentService.DeleteClassStudent(classStudentId: classStudentId);
}