// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Exposures;

public interface IStudentManager
{
    Task<Student> CreateAsync(Student model);
    Task<Student> ReadAsync(string id);
    Task<Student> UpdateAsync(Student model);
    Task DeleteAsync(string id);
}
