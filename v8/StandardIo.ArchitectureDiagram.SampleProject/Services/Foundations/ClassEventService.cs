// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Brokers.Eventings;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;

public sealed class ClassEventService : IClassEventService
{
    private readonly IClassEventBroker classEventBroker;

    public ClassEventService(IClassEventBroker classEventBroker)
    {
        this.classEventBroker = classEventBroker;
    }

    public ValueTask RaiseCreatedAsync(Class model) =>
        classEventBroker.RaiseCreatedAsync(model: model);

    public ValueTask RaiseReadAsync(Class model) =>
        classEventBroker.RaiseReadAsync(model: model);

    public ValueTask RaiseUpdatedAsync(Class model) =>
        classEventBroker.RaiseUpdatedAsync(model: model);

    public ValueTask RaiseDeletedAsync(Class model) =>
        classEventBroker.RaiseDeletedAsync(model: model);
}
