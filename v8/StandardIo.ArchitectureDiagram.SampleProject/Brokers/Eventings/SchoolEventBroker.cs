// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;
using cCoder.Eventing;
using cCoder.Eventing.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Brokers.Eventings;
internal sealed class SchoolEventBroker : ISchoolEventBroker
{
    private readonly IEventHub eventHub;
    public SchoolEventBroker(IEventHub eventHub)
    {
        this.eventHub = eventHub;
    }

    public ValueTask RaiseSchoolCreatedAsync(School school) =>
        eventHub.RaiseEventAsync(name: "School.Created", message: new EventMessage<School> { Data = school, AuthInfo = new EventAuthInfo() });

    public ValueTask RaiseSchoolReadAsync(School school) =>
        eventHub.RaiseEventAsync(name: "School.Read", message: new EventMessage<School> { Data = school, AuthInfo = new EventAuthInfo() });

    public ValueTask RaiseSchoolUpdatedAsync(School school) =>
        eventHub.RaiseEventAsync(name: "School.Updated", message: new EventMessage<School> { Data = school, AuthInfo = new EventAuthInfo() });

    public ValueTask RaiseSchoolDeletedAsync(School school) =>
        eventHub.RaiseEventAsync(name: "School.Deleted", message: new EventMessage<School> { Data = school, AuthInfo = new EventAuthInfo() });
}