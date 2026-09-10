// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using StandardIo.ArchitectureDiagram.SampleProject.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;

public interface IStudentService
{
    Student Create(Student model);
    Student Read(string id);
    Student Update(Student model);
    void Delete(string id);
}
