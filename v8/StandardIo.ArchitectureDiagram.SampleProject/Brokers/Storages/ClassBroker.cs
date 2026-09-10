// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using StandardIo.ArchitectureDiagram.SampleProject.Exposures;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Exposures;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Brokers.Storages;
internal sealed class ClassBroker : IClassBroker
{
    private readonly SchoolDataContext context;
    public ClassBroker(ISchoolFactory schoolFactory)
    {
        context = schoolFactory.CreateSchoolDataContext();
    }

    public Class CreateClass(Class @class)
    {
        context.Classes.Add(item: @class);
        return @class;
    }

    public Class ReadClass(string classId)
    {
        return context.Classes.Find(match: model => model.Id == classId) ?? throw new InvalidOperationException(message: "Class was not found.");
    }

    public Class UpdateClass(Class updatedClass)
    {
        int index = context.Classes.FindIndex(match: existing => existing.Id == updatedClass.Id);

        if (index < 0)
        {
            throw new InvalidOperationException(message: "Class was not found.");
        }

        context.Classes[index] = updatedClass;
        return updatedClass;
    }

    public void DeleteClass(string classId)
    {
        context.Classes.RemoveAll(match: model => model.Id == classId);
    }
}