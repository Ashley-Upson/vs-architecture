// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Processings;
internal interface ITeacherProcessingService
{
    Teacher CreateTeacher(Teacher teacher);

    Teacher ReadTeacher(string teacherId);

    Teacher UpdateTeacher(Teacher updatedTeacher);

    void DeleteTeacher(string teacherId);
}