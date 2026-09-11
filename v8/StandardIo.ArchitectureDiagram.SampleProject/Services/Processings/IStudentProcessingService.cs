// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Processings;
internal interface IStudentProcessingService
{
    Student CreateStudent(Student student);

    Student ReadStudent(string studentId);

    Student UpdateStudent(Student updatedStudent);

    void DeleteStudent(string studentId);
}