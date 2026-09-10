// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Models;
using cCoder.Eventing;
using cCoder.Eventing.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Brokers.Eventings;

public sealed class StudentEventBroker : IStudentEventBroker
{
    private readonly IEventHub eventHub;

    public StudentEventBroker(IEventHub eventHub)
    {
        this.eventHub = eventHub;
    }

    public ValueTask RaiseCreatedAsync(Student model) =>
        eventHub.RaiseEventAsync(name: "Student.Created", message: new EventMessage<Student>
        {
            Data = model,
            AuthInfo = new EventAuthInfo()
        });

    public ValueTask RaiseReadAsync(Student model) =>
        eventHub.RaiseEventAsync(name: "Student.Read", message: new EventMessage<Student>
        {
            Data = model,
            AuthInfo = new EventAuthInfo()
        });

    public ValueTask RaiseUpdatedAsync(Student model) =>
        eventHub.RaiseEventAsync(name: "Student.Updated", message: new EventMessage<Student>
        {
            Data = model,
            AuthInfo = new EventAuthInfo()
        });

    public ValueTask RaiseDeletedAsync(Student model) =>
        eventHub.RaiseEventAsync(name: "Student.Deleted", message: new EventMessage<Student>
        {
            Data = model,
            AuthInfo = new EventAuthInfo()
        });
}
