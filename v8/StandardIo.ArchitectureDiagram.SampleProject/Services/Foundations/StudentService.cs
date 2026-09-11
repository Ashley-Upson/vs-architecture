// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Brokers.Storages;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;
internal sealed class StudentService : IStudentService
{
    private readonly IStudentBroker studentBroker;
    public StudentService(IStudentBroker studentBroker)
    {
        this.studentBroker = studentBroker;
    }

    public Student CreateStudent(Student student) =>
        studentBroker.CreateStudent(student: student);

    public Student ReadStudent(string studentId) =>
        studentBroker.ReadStudent(studentId: studentId);

    public Student UpdateStudent(Student updatedStudent) =>
        studentBroker.UpdateStudent(updatedStudent: updatedStudent);

    public void DeleteStudent(string studentId) =>
        studentBroker.DeleteStudent(studentId: studentId);
}