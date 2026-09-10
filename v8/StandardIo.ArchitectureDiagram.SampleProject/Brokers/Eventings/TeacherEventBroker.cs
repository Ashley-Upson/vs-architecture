// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Models;
using cCoder.Eventing;
using cCoder.Eventing.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Brokers.Eventings;

public sealed class TeacherEventBroker : ITeacherEventBroker
{
    private readonly IEventHub eventHub;

    public TeacherEventBroker(IEventHub eventHub)
    {
        this.eventHub = eventHub;
    }

    public ValueTask RaiseCreatedAsync(Teacher model) =>
        eventHub.RaiseEventAsync(name: "Teacher.Created", message: new EventMessage<Teacher>
        {
            Data = model,
            AuthInfo = new EventAuthInfo()
        });

    public ValueTask RaiseReadAsync(Teacher model) =>
        eventHub.RaiseEventAsync(name: "Teacher.Read", message: new EventMessage<Teacher>
        {
            Data = model,
            AuthInfo = new EventAuthInfo()
        });

    public ValueTask RaiseUpdatedAsync(Teacher model) =>
        eventHub.RaiseEventAsync(name: "Teacher.Updated", message: new EventMessage<Teacher>
        {
            Data = model,
            AuthInfo = new EventAuthInfo()
        });

    public ValueTask RaiseDeletedAsync(Teacher model) =>
        eventHub.RaiseEventAsync(name: "Teacher.Deleted", message: new EventMessage<Teacher>
        {
            Data = model,
            AuthInfo = new EventAuthInfo()
        });
}
