// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Brokers.Storages;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;
internal sealed class SchoolService : ISchoolService
{
    private readonly ISchoolBroker schoolBroker;
    public SchoolService(ISchoolBroker schoolBroker)
    {
        this.schoolBroker = schoolBroker;
    }

    public School CreateSchool(School school) =>
        schoolBroker.CreateSchool(school: school);

    public School ReadSchool(string schoolId) =>
        schoolBroker.ReadSchool(schoolId: schoolId);

    public School UpdateSchool(School updatedSchool) =>
        schoolBroker.UpdateSchool(updatedSchool: updatedSchool);

    public void DeleteSchool(string schoolId) =>
        schoolBroker.DeleteSchool(schoolId: schoolId);
}