// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;
internal interface IClassStudentService
{
    ClassStudent CreateClassStudent(ClassStudent classStudent);

    ClassStudent ReadClassStudent(string classStudentId);

    ClassStudent UpdateClassStudent(ClassStudent updatedClassStudent);

    void DeleteClassStudent(string classStudentId);
}