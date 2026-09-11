// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using StandardIo.ArchitectureDiagram.SampleProject.Exposures;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Exposures;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Brokers.Storages;
internal sealed class ClassStudentBroker : IClassStudentBroker
{
    private readonly SchoolDataContext context;
    public ClassStudentBroker(ISchoolFactory schoolFactory)
    {
        context = schoolFactory.CreateSchoolDataContext();
    }

    public ClassStudent CreateClassStudent(ClassStudent classStudent)
    {
        context.ClassStudents.Add(item: classStudent);
        return classStudent;
    }

    public ClassStudent ReadClassStudent(string classStudentId)
    {
        return context.ClassStudents.Find(match: model => model.Id == classStudentId) ?? throw new InvalidOperationException(message: "ClassStudent was not found.");
    }

    public ClassStudent UpdateClassStudent(ClassStudent updatedClassStudent)
    {
        int index = context.ClassStudents.FindIndex(match: existing => existing.Id == updatedClassStudent.Id);

        if (index < 0)
        {
            throw new InvalidOperationException(message: "ClassStudent was not found.");
        }

        context.ClassStudents[index] = updatedClassStudent;
        return updatedClassStudent;
    }

    public void DeleteClassStudent(string classStudentId)
    {
        context.ClassStudents.RemoveAll(match: model => model.Id == classStudentId);
    }
}