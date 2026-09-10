// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Brokers.Storages;
internal interface ITeacherBroker
{
    Teacher CreateTeacher(Teacher teacher);

    Teacher ReadTeacher(string teacherId);

    Teacher UpdateTeacher(Teacher updatedTeacher);

    void DeleteTeacher(string teacherId);
}