// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Processings;
internal sealed class TeacherEventProcessingService : ITeacherEventProcessingService
{
    private readonly ITeacherEventService teacherEventService;
    public TeacherEventProcessingService(ITeacherEventService teacherEventService)
    {
        this.teacherEventService = teacherEventService;
    }

    public ValueTask RaiseTeacherCreatedAsync(Teacher teacher) =>
        teacherEventService.RaiseTeacherCreatedAsync(teacher: teacher);

    public ValueTask RaiseTeacherReadAsync(Teacher teacher) =>
        teacherEventService.RaiseTeacherReadAsync(teacher: teacher);

    public ValueTask RaiseTeacherUpdatedAsync(Teacher teacher) =>
        teacherEventService.RaiseTeacherUpdatedAsync(teacher: teacher);

    public ValueTask RaiseTeacherDeletedAsync(Teacher teacher) =>
        teacherEventService.RaiseTeacherDeletedAsync(teacher: teacher);
}