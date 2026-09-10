// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Models;
using cCoder.Eventing;
using cCoder.Eventing.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Brokers.Eventings;

public sealed class ClassStudentEventBroker : IClassStudentEventBroker
{
    private readonly IEventHub eventHub;

    public ClassStudentEventBroker(IEventHub eventHub)
    {
        this.eventHub = eventHub;
    }

    public ValueTask RaiseCreatedAsync(ClassStudent model) =>
        eventHub.RaiseEventAsync(name: "ClassStudent.Created", message: new EventMessage<ClassStudent>
        {
            Data = model,
            AuthInfo = new EventAuthInfo()
        });

    public ValueTask RaiseReadAsync(ClassStudent model) =>
        eventHub.RaiseEventAsync(name: "ClassStudent.Read", message: new EventMessage<ClassStudent>
        {
            Data = model,
            AuthInfo = new EventAuthInfo()
        });

    public ValueTask RaiseUpdatedAsync(ClassStudent model) =>
        eventHub.RaiseEventAsync(name: "ClassStudent.Updated", message: new EventMessage<ClassStudent>
        {
            Data = model,
            AuthInfo = new EventAuthInfo()
        });

    public ValueTask RaiseDeletedAsync(ClassStudent model) =>
        eventHub.RaiseEventAsync(name: "ClassStudent.Deleted", message: new EventMessage<ClassStudent>
        {
            Data = model,
            AuthInfo = new EventAuthInfo()
        });
}
