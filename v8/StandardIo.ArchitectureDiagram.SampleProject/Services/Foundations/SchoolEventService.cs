// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Brokers.Eventings;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;
internal sealed class SchoolEventService : ISchoolEventService
{
    private readonly ISchoolEventBroker schoolEventBroker;
    public SchoolEventService(ISchoolEventBroker schoolEventBroker)
    {
        this.schoolEventBroker = schoolEventBroker;
    }

    public ValueTask RaiseSchoolCreatedAsync(School school) =>
        schoolEventBroker.RaiseSchoolCreatedAsync(school: school);

    public ValueTask RaiseSchoolReadAsync(School school) =>
        schoolEventBroker.RaiseSchoolReadAsync(school: school);

    public ValueTask RaiseSchoolUpdatedAsync(School school) =>
        schoolEventBroker.RaiseSchoolUpdatedAsync(school: school);

    public ValueTask RaiseSchoolDeletedAsync(School school) =>
        schoolEventBroker.RaiseSchoolDeletedAsync(school: school);
}