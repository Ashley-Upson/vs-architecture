// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Brokers.Eventings;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;
internal sealed class ClassEventService : IClassEventService
{
    private readonly IClassEventBroker classEventBroker;
    public ClassEventService(IClassEventBroker classEventBroker)
    {
        this.classEventBroker = classEventBroker;
    }

    public ValueTask RaiseClassCreatedAsync(Class @class) =>
        classEventBroker.RaiseClassCreatedAsync(@class: @class);

    public ValueTask RaiseClassReadAsync(Class @class) =>
        classEventBroker.RaiseClassReadAsync(@class: @class);

    public ValueTask RaiseClassUpdatedAsync(Class @class) =>
        classEventBroker.RaiseClassUpdatedAsync(@class: @class);

    public ValueTask RaiseClassDeletedAsync(Class @class) =>
        classEventBroker.RaiseClassDeletedAsync(@class: @class);
}