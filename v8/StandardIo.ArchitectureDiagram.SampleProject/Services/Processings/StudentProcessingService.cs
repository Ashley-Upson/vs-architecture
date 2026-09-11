// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Processings;
internal sealed class StudentProcessingService : IStudentProcessingService
{
    private readonly IStudentService studentService;
    public StudentProcessingService(IStudentService studentService)
    {
        this.studentService = studentService;
    }

    public Student CreateStudent(Student student) =>
        studentService.CreateStudent(student: student);

    public Student ReadStudent(string studentId) =>
        studentService.ReadStudent(studentId: studentId);

    public Student UpdateStudent(Student updatedStudent) =>
        studentService.UpdateStudent(updatedStudent: updatedStudent);

    public void DeleteStudent(string studentId) =>
        studentService.DeleteStudent(studentId: studentId);
}