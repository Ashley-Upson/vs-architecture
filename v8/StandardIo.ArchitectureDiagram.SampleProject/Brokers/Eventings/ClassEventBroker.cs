// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Models;
using cCoder.Eventing;
using cCoder.Eventing.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Brokers.Eventings;

public sealed class ClassEventBroker : IClassEventBroker
{
    private readonly IEventHub eventHub;

    public ClassEventBroker(IEventHub eventHub)
    {
        this.eventHub = eventHub;
    }

    public ValueTask RaiseCreatedAsync(Class model) =>
        eventHub.RaiseEventAsync(name: "Class.Created", message: new EventMessage<Class>
        {
            Data = model,
            AuthInfo = new EventAuthInfo()
        });

    public ValueTask RaiseReadAsync(Class model) =>
        eventHub.RaiseEventAsync(name: "Class.Read", message: new EventMessage<Class>
        {
            Data = model,
            AuthInfo = new EventAuthInfo()
        });

    public ValueTask RaiseUpdatedAsync(Class model) =>
        eventHub.RaiseEventAsync(name: "Class.Updated", message: new EventMessage<Class>
        {
            Data = model,
            AuthInfo = new EventAuthInfo()
        });

    public ValueTask RaiseDeletedAsync(Class model) =>
        eventHub.RaiseEventAsync(name: "Class.Deleted", message: new EventMessage<Class>
        {
            Data = model,
            AuthInfo = new EventAuthInfo()
        });
}
