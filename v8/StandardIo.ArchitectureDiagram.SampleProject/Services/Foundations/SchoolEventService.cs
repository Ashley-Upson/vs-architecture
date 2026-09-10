// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Brokers.Eventings;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;

public sealed class SchoolEventService : ISchoolEventService
{
    private readonly ISchoolEventBroker schoolEventBroker;

    public SchoolEventService(ISchoolEventBroker schoolEventBroker)
    {
        this.schoolEventBroker = schoolEventBroker;
    }

    public ValueTask RaiseCreatedAsync(School model) =>
        schoolEventBroker.RaiseCreatedAsync(model: model);

    public ValueTask RaiseReadAsync(School model) =>
        schoolEventBroker.RaiseReadAsync(model: model);

    public ValueTask RaiseUpdatedAsync(School model) =>
        schoolEventBroker.RaiseUpdatedAsync(model: model);

    public ValueTask RaiseDeletedAsync(School model) =>
        schoolEventBroker.RaiseDeletedAsync(model: model);
}
