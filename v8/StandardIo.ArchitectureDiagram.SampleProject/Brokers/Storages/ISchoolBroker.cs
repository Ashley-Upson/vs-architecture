// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using StandardIo.ArchitectureDiagram.SampleProject.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Brokers.Storages;

public interface ISchoolBroker
{
    School Create(School model);
    School Read(string id);
    School Update(School model);
    void Delete(string id);
}
