// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Processings;
internal sealed class SchoolEventProcessingService : ISchoolEventProcessingService
{
    private readonly ISchoolEventService schoolEventService;
    public SchoolEventProcessingService(ISchoolEventService schoolEventService)
    {
        this.schoolEventService = schoolEventService;
    }

    public ValueTask RaiseSchoolCreatedAsync(School school) =>
        schoolEventService.RaiseSchoolCreatedAsync(school: school);

    public ValueTask RaiseSchoolReadAsync(School school) =>
        schoolEventService.RaiseSchoolReadAsync(school: school);

    public ValueTask RaiseSchoolUpdatedAsync(School school) =>
        schoolEventService.RaiseSchoolUpdatedAsync(school: school);

    public ValueTask RaiseSchoolDeletedAsync(School school) =>
        schoolEventService.RaiseSchoolDeletedAsync(school: school);
}