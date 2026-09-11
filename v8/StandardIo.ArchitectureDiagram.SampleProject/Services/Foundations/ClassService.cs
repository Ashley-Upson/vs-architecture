// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Brokers.Storages;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;
internal sealed class ClassService : IClassService
{
    private readonly IClassBroker classBroker;
    public ClassService(IClassBroker classBroker)
    {
        this.classBroker = classBroker;
    }

    public Class CreateClass(Class @class) =>
        classBroker.CreateClass(@class: @class);

    public Class ReadClass(string classId) =>
        classBroker.ReadClass(classId: classId);

    public Class UpdateClass(Class updatedClass) =>
        classBroker.UpdateClass(updatedClass: updatedClass);

    public void DeleteClass(string classId) =>
        classBroker.DeleteClass(classId: classId);
}