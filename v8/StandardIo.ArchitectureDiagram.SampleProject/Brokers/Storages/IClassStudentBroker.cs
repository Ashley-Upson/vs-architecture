// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using StandardIo.ArchitectureDiagram.SampleProject.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Brokers.Storages;

public interface IClassStudentBroker
{
    ClassStudent Create(ClassStudent model);
    ClassStudent Read(string id);
    ClassStudent Update(ClassStudent model);
    void Delete(string id);
}
