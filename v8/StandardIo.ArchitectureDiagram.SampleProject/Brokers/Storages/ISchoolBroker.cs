// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Brokers.Storages;
internal interface ISchoolBroker
{
    School CreateSchool(School school);

    School ReadSchool(string schoolId);

    School UpdateSchool(School updatedSchool);

    void DeleteSchool(string schoolId);
}