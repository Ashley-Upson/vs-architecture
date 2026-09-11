// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;
using cCoder.Eventing;
using cCoder.Eventing.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Brokers.Eventings;
internal sealed class ClassEventBroker : IClassEventBroker
{
    private readonly IEventHub eventHub;
    public ClassEventBroker(IEventHub eventHub)
    {
        this.eventHub = eventHub;
    }

    public ValueTask RaiseClassCreatedAsync(Class @class) =>
        eventHub.RaiseEventAsync(name: "Class.Created", message: new EventMessage<Class> { Data = @class, AuthInfo = new EventAuthInfo() });

    public ValueTask RaiseClassReadAsync(Class @class) =>
        eventHub.RaiseEventAsync(name: "Class.Read", message: new EventMessage<Class> { Data = @class, AuthInfo = new EventAuthInfo() });

    public ValueTask RaiseClassUpdatedAsync(Class @class) =>
        eventHub.RaiseEventAsync(name: "Class.Updated", message: new EventMessage<Class> { Data = @class, AuthInfo = new EventAuthInfo() });

    public ValueTask RaiseClassDeletedAsync(Class @class) =>
        eventHub.RaiseEventAsync(name: "Class.Deleted", message: new EventMessage<Class> { Data = @class, AuthInfo = new EventAuthInfo() });
}