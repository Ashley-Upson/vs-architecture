// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Processings;

public sealed class SchoolEventProcessingService : ISchoolEventProcessingService
{
    private readonly ISchoolEventService schoolEventService;

    public SchoolEventProcessingService(ISchoolEventService schoolEventService)
    {
        this.schoolEventService = schoolEventService;
    }

    public ValueTask RaiseCreatedAsync(School model) =>
        schoolEventService.RaiseCreatedAsync(model: model);

    public ValueTask RaiseReadAsync(School model) =>
        schoolEventService.RaiseReadAsync(model: model);

    public ValueTask RaiseUpdatedAsync(School model) =>
        schoolEventService.RaiseUpdatedAsync(model: model);

    public ValueTask RaiseDeletedAsync(School model) =>
        schoolEventService.RaiseDeletedAsync(model: model);
}
