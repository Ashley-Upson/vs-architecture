// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using StandardIo.ArchitectureDiagram.SampleProject.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Brokers.Storages;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;

public sealed class SchoolService : ISchoolService
{
    private readonly ISchoolBroker schoolBroker;

    public SchoolService(ISchoolBroker schoolBroker)
    {
        this.schoolBroker = schoolBroker;
    }

    public School Create(School model) => schoolBroker.Create(model: model);

    public School Read(string id) => schoolBroker.Read(id: id);

    public School Update(School model) => schoolBroker.Update(model: model);

    public void Delete(string id) => schoolBroker.Delete(id: id);
}
