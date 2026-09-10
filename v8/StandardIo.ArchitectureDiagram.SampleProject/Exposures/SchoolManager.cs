// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Services.Orchestrations;

namespace StandardIo.ArchitectureDiagram.SampleProject.Exposures;

public sealed class SchoolManager : ISchoolManager
{
    private readonly ISchoolOrchestrationService schoolOrchestrationService;

    public SchoolManager(ISchoolOrchestrationService schoolOrchestrationService)
    {
        this.schoolOrchestrationService = schoolOrchestrationService;
    }

    public Task<School> CreateAsync(School model) => schoolOrchestrationService.CreateAsync(model: model);

    public Task<School> ReadAsync(string id) => schoolOrchestrationService.ReadAsync(id: id);

    public Task<School> UpdateAsync(School model) => schoolOrchestrationService.UpdateAsync(model: model);

    public Task DeleteAsync(string id) => schoolOrchestrationService.DeleteAsync(id: id);
}
