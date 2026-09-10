// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Processings;

public sealed class TeacherEventProcessingService : ITeacherEventProcessingService
{
    private readonly ITeacherEventService teacherEventService;

    public TeacherEventProcessingService(ITeacherEventService teacherEventService)
    {
        this.teacherEventService = teacherEventService;
    }

    public ValueTask RaiseCreatedAsync(Teacher model) =>
        teacherEventService.RaiseCreatedAsync(model: model);

    public ValueTask RaiseReadAsync(Teacher model) =>
        teacherEventService.RaiseReadAsync(model: model);

    public ValueTask RaiseUpdatedAsync(Teacher model) =>
        teacherEventService.RaiseUpdatedAsync(model: model);

    public ValueTask RaiseDeletedAsync(Teacher model) =>
        teacherEventService.RaiseDeletedAsync(model: model);
}
