// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;
using cCoder.Eventing;
using cCoder.Eventing.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Brokers.Eventings;
internal sealed class TeacherEventBroker : ITeacherEventBroker
{
    private readonly IEventHub eventHub;
    public TeacherEventBroker(IEventHub eventHub)
    {
        this.eventHub = eventHub;
    }

    public ValueTask RaiseTeacherCreatedAsync(Teacher teacher) =>
        eventHub.RaiseEventAsync(name: "Teacher.Created", message: new EventMessage<Teacher> { Data = teacher, AuthInfo = new EventAuthInfo() });

    public ValueTask RaiseTeacherReadAsync(Teacher teacher) =>
        eventHub.RaiseEventAsync(name: "Teacher.Read", message: new EventMessage<Teacher> { Data = teacher, AuthInfo = new EventAuthInfo() });

    public ValueTask RaiseTeacherUpdatedAsync(Teacher teacher) =>
        eventHub.RaiseEventAsync(name: "Teacher.Updated", message: new EventMessage<Teacher> { Data = teacher, AuthInfo = new EventAuthInfo() });

    public ValueTask RaiseTeacherDeletedAsync(Teacher teacher) =>
        eventHub.RaiseEventAsync(name: "Teacher.Deleted", message: new EventMessage<Teacher> { Data = teacher, AuthInfo = new EventAuthInfo() });
}