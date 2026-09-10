// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using StandardIo.ArchitectureDiagram.SampleProject.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Brokers.Storages;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;

public sealed class ClassService : IClassService
{
    private readonly IClassBroker classBroker;

    public ClassService(IClassBroker classBroker)
    {
        this.classBroker = classBroker;
    }

    public Class Create(Class model) => classBroker.Create(model: model);

    public Class Read(string id) => classBroker.Read(id: id);

    public Class Update(Class model) => classBroker.Update(model: model);

    public void Delete(string id) => classBroker.Delete(id: id);
}
