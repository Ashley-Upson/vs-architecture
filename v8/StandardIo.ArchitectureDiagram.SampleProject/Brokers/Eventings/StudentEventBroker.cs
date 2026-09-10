// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;
using cCoder.Eventing;
using cCoder.Eventing.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Brokers.Eventings;
internal sealed class StudentEventBroker : IStudentEventBroker
{
    private readonly IEventHub eventHub;
    public StudentEventBroker(IEventHub eventHub)
    {
        this.eventHub = eventHub;
    }

    public ValueTask RaiseStudentCreatedAsync(Student student) =>
        eventHub.RaiseEventAsync(name: "Student.Created", message: new EventMessage<Student> { Data = student, AuthInfo = new EventAuthInfo() });

    public ValueTask RaiseStudentReadAsync(Student student) =>
        eventHub.RaiseEventAsync(name: "Student.Read", message: new EventMessage<Student> { Data = student, AuthInfo = new EventAuthInfo() });

    public ValueTask RaiseStudentUpdatedAsync(Student student) =>
        eventHub.RaiseEventAsync(name: "Student.Updated", message: new EventMessage<Student> { Data = student, AuthInfo = new EventAuthInfo() });

    public ValueTask RaiseStudentDeletedAsync(Student student) =>
        eventHub.RaiseEventAsync(name: "Student.Deleted", message: new EventMessage<Student> { Data = student, AuthInfo = new EventAuthInfo() });
}