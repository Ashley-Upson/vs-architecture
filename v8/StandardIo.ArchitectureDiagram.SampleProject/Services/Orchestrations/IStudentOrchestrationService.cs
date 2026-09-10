// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Orchestrations;

public interface IStudentOrchestrationService
{
    Task<Student> CreateAsync(Student model);
    Task<Student> ReadAsync(string id);
    Task<Student> UpdateAsync(Student model);
    Task DeleteAsync(string id);
}
