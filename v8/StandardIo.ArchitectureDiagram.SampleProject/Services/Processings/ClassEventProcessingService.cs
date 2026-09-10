// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Processings;
internal sealed class ClassEventProcessingService : IClassEventProcessingService
{
    private readonly IClassEventService classEventService;
    public ClassEventProcessingService(IClassEventService classEventService)
    {
        this.classEventService = classEventService;
    }

    public ValueTask RaiseClassCreatedAsync(Class @class) =>
        classEventService.RaiseClassCreatedAsync(@class: @class);

    public ValueTask RaiseClassReadAsync(Class @class) =>
        classEventService.RaiseClassReadAsync(@class: @class);

    public ValueTask RaiseClassUpdatedAsync(Class @class) =>
        classEventService.RaiseClassUpdatedAsync(@class: @class);

    public ValueTask RaiseClassDeletedAsync(Class @class) =>
        classEventService.RaiseClassDeletedAsync(@class: @class);
}