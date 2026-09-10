// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Processings;
internal sealed class TeacherProcessingService : ITeacherProcessingService
{
    private readonly ITeacherService teacherService;
    public TeacherProcessingService(ITeacherService teacherService)
    {
        this.teacherService = teacherService;
    }

    public Teacher CreateTeacher(Teacher teacher) =>
        teacherService.CreateTeacher(teacher: teacher);

    public Teacher ReadTeacher(string teacherId) =>
        teacherService.ReadTeacher(teacherId: teacherId);

    public Teacher UpdateTeacher(Teacher updatedTeacher) =>
        teacherService.UpdateTeacher(updatedTeacher: updatedTeacher);

    public void DeleteTeacher(string teacherId) =>
        teacherService.DeleteTeacher(teacherId: teacherId);
}