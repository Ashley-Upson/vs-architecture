// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using StandardIo.ArchitectureDiagram.SampleProject.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Brokers.Storages;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;

public sealed class TeacherService : ITeacherService
{
    private readonly ITeacherBroker teacherBroker;

    public TeacherService(ITeacherBroker teacherBroker)
    {
        this.teacherBroker = teacherBroker;
    }

    public Teacher Create(Teacher model) => teacherBroker.Create(model: model);

    public Teacher Read(string id) => teacherBroker.Read(id: id);

    public Teacher Update(Teacher model) => teacherBroker.Update(model: model);

    public void Delete(string id) => teacherBroker.Delete(id: id);
}
