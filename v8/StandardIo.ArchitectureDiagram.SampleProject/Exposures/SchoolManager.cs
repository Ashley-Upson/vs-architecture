// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Services.Orchestrations;

namespace StandardIo.ArchitectureDiagram.SampleProject.Exposures;
public sealed class SchoolManager : ISchoolManager
{
    private readonly ISchoolOrchestrationService schoolOrchestrationService;
    internal SchoolManager(ISchoolOrchestrationService schoolOrchestrationService)
    {
        this.schoolOrchestrationService = schoolOrchestrationService;
    }

    public Task<School> CreateSchoolAsync(School school) =>
        schoolOrchestrationService.CreateSchoolAsync(school: school);

    public Task<School> ReadSchoolAsync(string schoolId) =>
        schoolOrchestrationService.ReadSchoolAsync(schoolId: schoolId);

    public Task<School> UpdateSchoolAsync(School updatedSchool) =>
        schoolOrchestrationService.UpdateSchoolAsync(updatedSchool: updatedSchool);

    public Task DeleteSchoolAsync(string schoolId) =>
        schoolOrchestrationService.DeleteSchoolAsync(schoolId: schoolId);
}