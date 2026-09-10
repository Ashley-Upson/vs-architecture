// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Orchestrations;
internal interface IStudentOrchestrationService
{
    Task<Student> CreateStudentAsync(Student student);

    Task<Student> ReadStudentAsync(string studentId);

    Task<Student> UpdateStudentAsync(Student updatedStudent);

    Task DeleteStudentAsync(string studentId);
}