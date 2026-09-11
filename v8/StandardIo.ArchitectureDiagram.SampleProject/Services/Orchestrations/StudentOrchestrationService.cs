// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Services.Processings;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Orchestrations;
internal sealed class StudentOrchestrationService : IStudentOrchestrationService
{
    private readonly IStudentProcessingService studentProcessingService;
    private readonly IStudentEventProcessingService studentEventProcessingService;
    public StudentOrchestrationService(IStudentProcessingService studentProcessingService, IStudentEventProcessingService studentEventProcessingService)
    {
        this.studentProcessingService = studentProcessingService;
        this.studentEventProcessingService = studentEventProcessingService;
    }

    public async Task<Student> CreateStudentAsync(Student student)
    {
        Student result = studentProcessingService.CreateStudent(student: student);
        await studentEventProcessingService.RaiseStudentCreatedAsync(student: result);
        return result;
    }

    public async Task<Student> ReadStudentAsync(string studentId)
    {
        Student result = studentProcessingService.ReadStudent(studentId: studentId);
        await studentEventProcessingService.RaiseStudentReadAsync(student: result);
        return result;
    }

    public async Task<Student> UpdateStudentAsync(Student updatedStudent)
    {
        Student result = studentProcessingService.UpdateStudent(updatedStudent: updatedStudent);
        await studentEventProcessingService.RaiseStudentUpdatedAsync(student: result);
        return result;
    }

    public async Task DeleteStudentAsync(string studentId)
    {
        Student model = studentProcessingService.ReadStudent(studentId: studentId);
        studentProcessingService.DeleteStudent(studentId: studentId);
        await studentEventProcessingService.RaiseStudentDeletedAsync(student: model);
    }
}