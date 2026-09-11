// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Processings;
internal sealed class SchoolProcessingService : ISchoolProcessingService
{
    private readonly ISchoolService schoolService;
    public SchoolProcessingService(ISchoolService schoolService)
    {
        this.schoolService = schoolService;
    }

    public School CreateSchool(School school) =>
        schoolService.CreateSchool(school: school);

    public School ReadSchool(string schoolId) =>
        schoolService.ReadSchool(schoolId: schoolId);

    public School UpdateSchool(School updatedSchool) =>
        schoolService.UpdateSchool(updatedSchool: updatedSchool);

    public void DeleteSchool(string schoolId) =>
        schoolService.DeleteSchool(schoolId: schoolId);
}