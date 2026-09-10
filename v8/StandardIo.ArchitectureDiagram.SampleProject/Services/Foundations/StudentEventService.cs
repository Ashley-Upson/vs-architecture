// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Brokers.Eventings;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;

public sealed class StudentEventService : IStudentEventService
{
    private readonly IStudentEventBroker studentEventBroker;

    public StudentEventService(IStudentEventBroker studentEventBroker)
    {
        this.studentEventBroker = studentEventBroker;
    }

    public ValueTask RaiseCreatedAsync(Student model) =>
        studentEventBroker.RaiseCreatedAsync(model: model);

    public ValueTask RaiseReadAsync(Student model) =>
        studentEventBroker.RaiseReadAsync(model: model);

    public ValueTask RaiseUpdatedAsync(Student model) =>
        studentEventBroker.RaiseUpdatedAsync(model: model);

    public ValueTask RaiseDeletedAsync(Student model) =>
        studentEventBroker.RaiseDeletedAsync(model: model);
}
