// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Brokers.Eventings;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;

public sealed class TeacherEventService : ITeacherEventService
{
    private readonly ITeacherEventBroker teacherEventBroker;

    public TeacherEventService(ITeacherEventBroker teacherEventBroker)
    {
        this.teacherEventBroker = teacherEventBroker;
    }

    public ValueTask RaiseCreatedAsync(Teacher model) =>
        teacherEventBroker.RaiseCreatedAsync(model: model);

    public ValueTask RaiseReadAsync(Teacher model) =>
        teacherEventBroker.RaiseReadAsync(model: model);

    public ValueTask RaiseUpdatedAsync(Teacher model) =>
        teacherEventBroker.RaiseUpdatedAsync(model: model);

    public ValueTask RaiseDeletedAsync(Teacher model) =>
        teacherEventBroker.RaiseDeletedAsync(model: model);
}
