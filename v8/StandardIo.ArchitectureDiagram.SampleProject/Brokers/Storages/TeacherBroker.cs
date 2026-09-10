// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System;
using StandardIo.ArchitectureDiagram.SampleProject.Exposures;
using StandardIo.ArchitectureDiagram.SampleProject.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Brokers.Storages;

public sealed class TeacherBroker : ITeacherBroker
{
    private readonly SchoolDataContext context;

    public TeacherBroker(ISchoolFactory schoolFactory)
    {
        context = schoolFactory.Create();
    }

    public Teacher Create(Teacher model)
    {
        context.Teachers.Add(item: model);
        return model;
    }

    public Teacher Read(string id)
    {
        return context.Teachers.Find(match: model => model.Id == id)
            ?? throw new InvalidOperationException(message: "Teacher was not found.");
    }

    public Teacher Update(Teacher model)
    {
        int index = context.Teachers.FindIndex(match: existing => existing.Id == model.Id);

        if (index < 0)
        {
            throw new InvalidOperationException(message: "Teacher was not found.");
        }

        context.Teachers[index] = model;
        return model;
    }

    public void Delete(string id)
    {
        context.Teachers.RemoveAll(match: model => model.Id == id);
    }
}