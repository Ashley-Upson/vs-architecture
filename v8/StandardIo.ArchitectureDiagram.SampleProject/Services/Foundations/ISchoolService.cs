// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;
internal interface ISchoolService
{
    School CreateSchool(School school);

    School ReadSchool(string schoolId);

    School UpdateSchool(School updatedSchool);

    void DeleteSchool(string schoolId);
}