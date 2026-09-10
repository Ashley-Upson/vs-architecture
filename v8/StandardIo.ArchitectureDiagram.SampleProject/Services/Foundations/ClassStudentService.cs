// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Brokers.Storages;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;
internal sealed class ClassStudentService : IClassStudentService
{
    private readonly IClassStudentBroker classStudentBroker;
    public ClassStudentService(IClassStudentBroker classStudentBroker)
    {
        this.classStudentBroker = classStudentBroker;
    }

    public ClassStudent CreateClassStudent(ClassStudent classStudent) =>
        classStudentBroker.CreateClassStudent(classStudent: classStudent);

    public ClassStudent ReadClassStudent(string classStudentId) =>
        classStudentBroker.ReadClassStudent(classStudentId: classStudentId);

    public ClassStudent UpdateClassStudent(ClassStudent updatedClassStudent) =>
        classStudentBroker.UpdateClassStudent(updatedClassStudent: updatedClassStudent);

    public void DeleteClassStudent(string classStudentId) =>
        classStudentBroker.DeleteClassStudent(classStudentId: classStudentId);
}