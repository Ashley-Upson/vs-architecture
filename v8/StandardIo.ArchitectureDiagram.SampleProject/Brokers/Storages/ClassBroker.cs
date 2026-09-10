// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System;
using StandardIo.ArchitectureDiagram.SampleProject.Exposures;
using StandardIo.ArchitectureDiagram.SampleProject.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Brokers.Storages;

public sealed class ClassBroker : IClassBroker
{
    private readonly SchoolDataContext context;

    public ClassBroker(ISchoolFactory schoolFactory)
    {
        context = schoolFactory.Create();
    }

    public Class Create(Class model)
    {
        context.Classes.Add(item: model);
        return model;
    }

    public Class Read(string id)
    {
        return context.Classes.Find(match: model => model.Id == id)
            ?? throw new InvalidOperationException(message: "Class was not found.");
    }

    public Class Update(Class model)
    {
        int index = context.Classes.FindIndex(match: existing => existing.Id == model.Id);

        if (index < 0)
        {
            throw new InvalidOperationException(message: "Class was not found.");
        }

        context.Classes[index] = model;
        return model;
    }

    public void Delete(string id)
    {
        context.Classes.RemoveAll(match: model => model.Id == id);
    }
}