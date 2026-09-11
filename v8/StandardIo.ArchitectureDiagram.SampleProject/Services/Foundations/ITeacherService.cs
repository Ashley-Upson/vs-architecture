// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;
internal interface ITeacherService
{
    Teacher CreateTeacher(Teacher teacher);

    Teacher ReadTeacher(string teacherId);

    Teacher UpdateTeacher(Teacher updatedTeacher);

    void DeleteTeacher(string teacherId);
}