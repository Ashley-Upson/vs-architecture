// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Brokers.Eventings;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;
internal sealed class StudentEventService : IStudentEventService
{
    private readonly IStudentEventBroker studentEventBroker;
    public StudentEventService(IStudentEventBroker studentEventBroker)
    {
        this.studentEventBroker = studentEventBroker;
    }

    public ValueTask RaiseStudentCreatedAsync(Student student) =>
        studentEventBroker.RaiseStudentCreatedAsync(student: student);

    public ValueTask RaiseStudentReadAsync(Student student) =>
        studentEventBroker.RaiseStudentReadAsync(student: student);

    public ValueTask RaiseStudentUpdatedAsync(Student student) =>
        studentEventBroker.RaiseStudentUpdatedAsync(student: student);

    public ValueTask RaiseStudentDeletedAsync(Student student) =>
        studentEventBroker.RaiseStudentDeletedAsync(student: student);
}