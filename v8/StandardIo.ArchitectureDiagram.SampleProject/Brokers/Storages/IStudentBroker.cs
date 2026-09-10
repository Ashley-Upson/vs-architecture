// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Brokers.Storages;
internal interface IStudentBroker
{
    Student CreateStudent(Student student);

    Student ReadStudent(string studentId);

    Student UpdateStudent(Student updatedStudent);

    void DeleteStudent(string studentId);
}