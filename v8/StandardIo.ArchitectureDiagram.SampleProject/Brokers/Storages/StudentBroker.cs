// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using StandardIo.ArchitectureDiagram.SampleProject.Exposures;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Exposures;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Brokers.Storages;
internal sealed class StudentBroker : IStudentBroker
{
    private readonly SchoolDataContext context;
    public StudentBroker(ISchoolFactory schoolFactory)
    {
        context = schoolFactory.CreateSchoolDataContext();
    }

    public Student CreateStudent(Student student)
    {
        context.Students.Add(item: student);
        return student;
    }

    public Student ReadStudent(string studentId)
    {
        return context.Students.Find(match: model => model.Id == studentId) ?? throw new InvalidOperationException(message: "Student was not found.");
    }

    public Student UpdateStudent(Student updatedStudent)
    {
        int index = context.Students.FindIndex(match: existing => existing.Id == updatedStudent.Id);

        if (index < 0)
        {
            throw new InvalidOperationException(message: "Student was not found.");
        }

        context.Students[index] = updatedStudent;
        return updatedStudent;
    }

    public void DeleteStudent(string studentId)
    {
        context.Students.RemoveAll(match: model => model.Id == studentId);
    }
}