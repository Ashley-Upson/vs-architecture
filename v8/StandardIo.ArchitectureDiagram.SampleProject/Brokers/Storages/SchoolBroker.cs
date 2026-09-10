// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using StandardIo.ArchitectureDiagram.SampleProject.Exposures;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Exposures;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Brokers.Storages;
internal sealed class SchoolBroker : ISchoolBroker
{
    private readonly SchoolDataContext context;
    public SchoolBroker(ISchoolFactory schoolFactory)
    {
        context = schoolFactory.CreateSchoolDataContext();
    }

    public School CreateSchool(School school)
    {
        context.Schools.Add(item: school);
        return school;
    }

    public School ReadSchool(string schoolId)
    {
        return context.Schools.Find(match: model => model.Id == schoolId) ?? throw new InvalidOperationException(message: "School was not found.");
    }

    public School UpdateSchool(School updatedSchool)
    {
        int index = context.Schools.FindIndex(match: existing => existing.Id == updatedSchool.Id);

        if (index < 0)
        {
            throw new InvalidOperationException(message: "School was not found.");
        }

        context.Schools[index] = updatedSchool;
        return updatedSchool;
    }

    public void DeleteSchool(string schoolId)
    {
        context.Schools.RemoveAll(match: model => model.Id == schoolId);
    }
}