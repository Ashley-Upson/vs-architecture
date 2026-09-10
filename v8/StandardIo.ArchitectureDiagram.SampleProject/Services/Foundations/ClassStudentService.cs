// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using StandardIo.ArchitectureDiagram.SampleProject.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Brokers.Storages;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;

public sealed class ClassStudentService : IClassStudentService
{
    private readonly IClassStudentBroker classStudentBroker;

    public ClassStudentService(IClassStudentBroker classStudentBroker)
    {
        this.classStudentBroker = classStudentBroker;
    }

    public ClassStudent Create(ClassStudent model) => classStudentBroker.Create(model: model);

    public ClassStudent Read(string id) => classStudentBroker.Read(id: id);

    public ClassStudent Update(ClassStudent model) => classStudentBroker.Update(model: model);

    public void Delete(string id) => classStudentBroker.Delete(id: id);
}
