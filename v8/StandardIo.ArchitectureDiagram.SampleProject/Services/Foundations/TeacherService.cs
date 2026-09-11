// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Brokers.Storages;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;
internal sealed class TeacherService : ITeacherService
{
    private readonly ITeacherBroker teacherBroker;
    public TeacherService(ITeacherBroker teacherBroker)
    {
        this.teacherBroker = teacherBroker;
    }

    public Teacher CreateTeacher(Teacher teacher) =>
        teacherBroker.CreateTeacher(teacher: teacher);

    public Teacher ReadTeacher(string teacherId) =>
        teacherBroker.ReadTeacher(teacherId: teacherId);

    public Teacher UpdateTeacher(Teacher updatedTeacher) =>
        teacherBroker.UpdateTeacher(updatedTeacher: updatedTeacher);

    public void DeleteTeacher(string teacherId) =>
        teacherBroker.DeleteTeacher(teacherId: teacherId);
}