// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Brokers.Eventings;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;

public sealed class ClassStudentEventService : IClassStudentEventService
{
    private readonly IClassStudentEventBroker classStudentEventBroker;

    public ClassStudentEventService(IClassStudentEventBroker classStudentEventBroker)
    {
        this.classStudentEventBroker = classStudentEventBroker;
    }

    public ValueTask RaiseCreatedAsync(ClassStudent model) =>
        classStudentEventBroker.RaiseCreatedAsync(model: model);

    public ValueTask RaiseReadAsync(ClassStudent model) =>
        classStudentEventBroker.RaiseReadAsync(model: model);

    public ValueTask RaiseUpdatedAsync(ClassStudent model) =>
        classStudentEventBroker.RaiseUpdatedAsync(model: model);

    public ValueTask RaiseDeletedAsync(ClassStudent model) =>
        classStudentEventBroker.RaiseDeletedAsync(model: model);
}
