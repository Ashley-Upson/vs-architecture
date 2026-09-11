// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Brokers.Eventings;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;
internal sealed class TeacherEventService : ITeacherEventService
{
    private readonly ITeacherEventBroker teacherEventBroker;
    public TeacherEventService(ITeacherEventBroker teacherEventBroker)
    {
        this.teacherEventBroker = teacherEventBroker;
    }

    public ValueTask RaiseTeacherCreatedAsync(Teacher teacher) =>
        teacherEventBroker.RaiseTeacherCreatedAsync(teacher: teacher);

    public ValueTask RaiseTeacherReadAsync(Teacher teacher) =>
        teacherEventBroker.RaiseTeacherReadAsync(teacher: teacher);

    public ValueTask RaiseTeacherUpdatedAsync(Teacher teacher) =>
        teacherEventBroker.RaiseTeacherUpdatedAsync(teacher: teacher);

    public ValueTask RaiseTeacherDeletedAsync(Teacher teacher) =>
        teacherEventBroker.RaiseTeacherDeletedAsync(teacher: teacher);
}