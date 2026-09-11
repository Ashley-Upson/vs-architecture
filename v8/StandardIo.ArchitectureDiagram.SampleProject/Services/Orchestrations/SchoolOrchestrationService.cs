// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Services.Processings;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Orchestrations;
internal sealed class SchoolOrchestrationService : ISchoolOrchestrationService
{
    private readonly ISchoolProcessingService schoolProcessingService;
    private readonly ISchoolEventProcessingService schoolEventProcessingService;
    public SchoolOrchestrationService(ISchoolProcessingService schoolProcessingService, ISchoolEventProcessingService schoolEventProcessingService)
    {
        this.schoolProcessingService = schoolProcessingService;
        this.schoolEventProcessingService = schoolEventProcessingService;
    }

    public async Task<School> CreateSchoolAsync(School school)
    {
        School result = schoolProcessingService.CreateSchool(school: school);
        await schoolEventProcessingService.RaiseSchoolCreatedAsync(school: result);
        return result;
    }

    public async Task<School> ReadSchoolAsync(string schoolId)
    {
        School result = schoolProcessingService.ReadSchool(schoolId: schoolId);
        await schoolEventProcessingService.RaiseSchoolReadAsync(school: result);
        return result;
    }

    public async Task<School> UpdateSchoolAsync(School updatedSchool)
    {
        School result = schoolProcessingService.UpdateSchool(updatedSchool: updatedSchool);
        await schoolEventProcessingService.RaiseSchoolUpdatedAsync(school: result);
        return result;
    }

    public async Task DeleteSchoolAsync(string schoolId)
    {
        School model = schoolProcessingService.ReadSchool(schoolId: schoolId);
        schoolProcessingService.DeleteSchool(schoolId: schoolId);
        await schoolEventProcessingService.RaiseSchoolDeletedAsync(school: model);
    }
}