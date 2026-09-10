// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Processings;

public sealed class ClassEventProcessingService : IClassEventProcessingService
{
    private readonly IClassEventService classEventService;

    public ClassEventProcessingService(IClassEventService classEventService)
    {
        this.classEventService = classEventService;
    }

    public ValueTask RaiseCreatedAsync(Class model) =>
        classEventService.RaiseCreatedAsync(model: model);

    public ValueTask RaiseReadAsync(Class model) =>
        classEventService.RaiseReadAsync(model: model);

    public ValueTask RaiseUpdatedAsync(Class model) =>
        classEventService.RaiseUpdatedAsync(model: model);

    public ValueTask RaiseDeletedAsync(Class model) =>
        classEventService.RaiseDeletedAsync(model: model);
}
