// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Services.Processings;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Orchestrations;

public sealed class SchoolOrchestrationService : ISchoolOrchestrationService
{
    private readonly ISchoolProcessingService schoolProcessingService;
    private readonly ISchoolEventProcessingService schoolEventProcessingService;

    public SchoolOrchestrationService(
        ISchoolProcessingService schoolProcessingService,
        ISchoolEventProcessingService schoolEventProcessingService)
    {
        this.schoolProcessingService = schoolProcessingService;
        this.schoolEventProcessingService = schoolEventProcessingService;
    }

    public async Task<School> CreateAsync(School model)
    {
        School result = schoolProcessingService.Create(model: model);
        await schoolEventProcessingService.RaiseCreatedAsync(model: result);
        return result;
    }

    public async Task<School> ReadAsync(string id)
    {
        School result = schoolProcessingService.Read(id: id);
        await schoolEventProcessingService.RaiseReadAsync(model: result);
        return result;
    }

    public async Task<School> UpdateAsync(School model)
    {
        School result = schoolProcessingService.Update(model: model);
        await schoolEventProcessingService.RaiseUpdatedAsync(model: result);
        return result;
    }

    public async Task DeleteAsync(string id)
    {
        School model = schoolProcessingService.Read(id: id);
        schoolProcessingService.Delete(id: id);
        await schoolEventProcessingService.RaiseDeletedAsync(model: model);
    }
}
