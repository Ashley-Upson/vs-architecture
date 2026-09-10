// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Processings;

public sealed class ClassStudentEventProcessingService : IClassStudentEventProcessingService
{
    private readonly IClassStudentEventService classStudentEventService;

    public ClassStudentEventProcessingService(IClassStudentEventService classStudentEventService)
    {
        this.classStudentEventService = classStudentEventService;
    }

    public ValueTask RaiseCreatedAsync(ClassStudent model) =>
        classStudentEventService.RaiseCreatedAsync(model: model);

    public ValueTask RaiseReadAsync(ClassStudent model) =>
        classStudentEventService.RaiseReadAsync(model: model);

    public ValueTask RaiseUpdatedAsync(ClassStudent model) =>
        classStudentEventService.RaiseUpdatedAsync(model: model);

    public ValueTask RaiseDeletedAsync(ClassStudent model) =>
        classStudentEventService.RaiseDeletedAsync(model: model);
}
