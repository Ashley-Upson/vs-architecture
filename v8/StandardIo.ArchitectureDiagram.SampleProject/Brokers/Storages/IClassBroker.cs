// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Brokers.Storages;
internal interface IClassBroker
{
    Class CreateClass(Class @class);

    Class ReadClass(string classId);

    Class UpdateClass(Class updatedClass);

    void DeleteClass(string classId);
}