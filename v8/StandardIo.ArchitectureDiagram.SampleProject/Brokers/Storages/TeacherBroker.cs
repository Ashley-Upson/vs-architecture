// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using StandardIo.ArchitectureDiagram.SampleProject.Exposures;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Exposures;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Brokers.Storages;
internal sealed class TeacherBroker : ITeacherBroker
{
    private readonly SchoolDataContext context;
    public TeacherBroker(ISchoolFactory schoolFactory)
    {
        context = schoolFactory.CreateSchoolDataContext();
    }

    public Teacher CreateTeacher(Teacher teacher)
    {
        context.Teachers.Add(item: teacher);
        return teacher;
    }

    public Teacher ReadTeacher(string teacherId)
    {
        return context.Teachers.Find(match: model => model.Id == teacherId) ?? throw new InvalidOperationException(message: "Teacher was not found.");
    }

    public Teacher UpdateTeacher(Teacher updatedTeacher)
    {
        int index = context.Teachers.FindIndex(match: existing => existing.Id == updatedTeacher.Id);

        if (index < 0)
        {
            throw new InvalidOperationException(message: "Teacher was not found.");
        }

        context.Teachers[index] = updatedTeacher;
        return updatedTeacher;
    }

    public void DeleteTeacher(string teacherId)
    {
        context.Teachers.RemoveAll(match: model => model.Id == teacherId);
    }
}