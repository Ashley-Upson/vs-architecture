// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;
using cCoder.Eventing;
using cCoder.Eventing.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Brokers.Eventings;
internal sealed class ClassStudentEventBroker : IClassStudentEventBroker
{
    private readonly IEventHub eventHub;
    public ClassStudentEventBroker(IEventHub eventHub)
    {
        this.eventHub = eventHub;
    }

    public ValueTask RaiseClassStudentCreatedAsync(ClassStudent classStudent) =>
        eventHub.RaiseEventAsync(name: "ClassStudent.Created", message: new EventMessage<ClassStudent> { Data = classStudent, AuthInfo = new EventAuthInfo() });

    public ValueTask RaiseClassStudentReadAsync(ClassStudent classStudent) =>
        eventHub.RaiseEventAsync(name: "ClassStudent.Read", message: new EventMessage<ClassStudent> { Data = classStudent, AuthInfo = new EventAuthInfo() });

    public ValueTask RaiseClassStudentUpdatedAsync(ClassStudent classStudent) =>
        eventHub.RaiseEventAsync(name: "ClassStudent.Updated", message: new EventMessage<ClassStudent> { Data = classStudent, AuthInfo = new EventAuthInfo() });

    public ValueTask RaiseClassStudentDeletedAsync(ClassStudent classStudent) =>
        eventHub.RaiseEventAsync(name: "ClassStudent.Deleted", message: new EventMessage<ClassStudent> { Data = classStudent, AuthInfo = new EventAuthInfo() });
}