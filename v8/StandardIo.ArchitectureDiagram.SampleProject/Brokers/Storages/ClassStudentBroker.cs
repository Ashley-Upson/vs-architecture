// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System;
using StandardIo.ArchitectureDiagram.SampleProject.Exposures;
using StandardIo.ArchitectureDiagram.SampleProject.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Brokers.Storages;

public sealed class ClassStudentBroker : IClassStudentBroker
{
    private readonly SchoolDataContext context;

    public ClassStudentBroker(ISchoolFactory schoolFactory)
    {
        context = schoolFactory.Create();
    }

    public ClassStudent Create(ClassStudent model)
    {
        context.ClassStudents.Add(item: model);
        return model;
    }

    public ClassStudent Read(string id)
    {
        return context.ClassStudents.Find(match: model => model.Id == id)
            ?? throw new InvalidOperationException(message: "ClassStudent was not found.");
    }

    public ClassStudent Update(ClassStudent model)
    {
        int index = context.ClassStudents.FindIndex(match: existing => existing.Id == model.Id);

        if (index < 0)
        {
            throw new InvalidOperationException(message: "ClassStudent was not found.");
        }

        context.ClassStudents[index] = model;
        return model;
    }

    public void Delete(string id)
    {
        context.ClassStudents.RemoveAll(match: model => model.Id == id);
    }
}