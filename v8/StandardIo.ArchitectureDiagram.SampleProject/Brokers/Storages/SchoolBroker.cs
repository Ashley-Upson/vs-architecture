// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System;
using StandardIo.ArchitectureDiagram.SampleProject.Exposures;
using StandardIo.ArchitectureDiagram.SampleProject.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Brokers.Storages;

public sealed class SchoolBroker : ISchoolBroker
{
    private readonly SchoolDataContext context;

    public SchoolBroker(ISchoolFactory schoolFactory)
    {
        context = schoolFactory.Create();
    }

    public School Create(School model)
    {
        context.Schools.Add(item: model);
        return model;
    }

    public School Read(string id)
    {
        return context.Schools.Find(match: model => model.Id == id)
            ?? throw new InvalidOperationException(message: "School was not found.");
    }

    public School Update(School model)
    {
        int index = context.Schools.FindIndex(match: existing => existing.Id == model.Id);

        if (index < 0)
        {
            throw new InvalidOperationException(message: "School was not found.");
        }

        context.Schools[index] = model;
        return model;
    }

    public void Delete(string id)
    {
        context.Schools.RemoveAll(match: model => model.Id == id);
    }
}