// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Models;
using cCoder.Eventing;
using cCoder.Eventing.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Brokers.Eventings;

public sealed class SchoolEventBroker : ISchoolEventBroker
{
    private readonly IEventHub eventHub;

    public SchoolEventBroker(IEventHub eventHub)
    {
        this.eventHub = eventHub;
    }

    public ValueTask RaiseCreatedAsync(School model) =>
        eventHub.RaiseEventAsync(name: "School.Created", message: new EventMessage<School>
        {
            Data = model,
            AuthInfo = new EventAuthInfo()
        });

    public ValueTask RaiseReadAsync(School model) =>
        eventHub.RaiseEventAsync(name: "School.Read", message: new EventMessage<School>
        {
            Data = model,
            AuthInfo = new EventAuthInfo()
        });

    public ValueTask RaiseUpdatedAsync(School model) =>
        eventHub.RaiseEventAsync(name: "School.Updated", message: new EventMessage<School>
        {
            Data = model,
            AuthInfo = new EventAuthInfo()
        });

    public ValueTask RaiseDeletedAsync(School model) =>
        eventHub.RaiseEventAsync(name: "School.Deleted", message: new EventMessage<School>
        {
            Data = model,
            AuthInfo = new EventAuthInfo()
        });
}
