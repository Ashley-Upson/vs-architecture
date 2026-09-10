// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System;
using StandardIo.ArchitectureDiagram.SampleProject.Exposures;
using StandardIo.ArchitectureDiagram.SampleProject.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Brokers.Storages;

public sealed class StudentBroker : IStudentBroker
{
    private readonly SchoolDataContext context;

    public StudentBroker(ISchoolFactory schoolFactory)
    {
        context = schoolFactory.Create();
    }

    public Student Create(Student model)
    {
        context.Students.Add(item: model);
        return model;
    }

    public Student Read(string id)
    {
        return context.Students.Find(match: model => model.Id == id)
            ?? throw new InvalidOperationException(message: "Student was not found.");
    }

    public Student Update(Student model)
    {
        int index = context.Students.FindIndex(match: existing => existing.Id == model.Id);

        if (index < 0)
        {
            throw new InvalidOperationException(message: "Student was not found.");
        }

        context.Students[index] = model;
        return model;
    }

    public void Delete(string id)
    {
        context.Students.RemoveAll(match: model => model.Id == id);
    }
}