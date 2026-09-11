// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Services.Orchestrations;

namespace StandardIo.ArchitectureDiagram.SampleProject.Exposures;
public sealed class StudentManager : IStudentManager
{
    private readonly IStudentOrchestrationService studentOrchestrationService;
    internal StudentManager(IStudentOrchestrationService studentOrchestrationService)
    {
        this.studentOrchestrationService = studentOrchestrationService;
    }

    public Task<Student> CreateStudentAsync(Student student) =>
        studentOrchestrationService.CreateStudentAsync(student: student);

    public Task<Student> ReadStudentAsync(string studentId) =>
        studentOrchestrationService.ReadStudentAsync(studentId: studentId);

    public Task<Student> UpdateStudentAsync(Student updatedStudent) =>
        studentOrchestrationService.UpdateStudentAsync(updatedStudent: updatedStudent);

    public Task DeleteStudentAsync(string studentId) =>
        studentOrchestrationService.DeleteStudentAsync(studentId: studentId);
}