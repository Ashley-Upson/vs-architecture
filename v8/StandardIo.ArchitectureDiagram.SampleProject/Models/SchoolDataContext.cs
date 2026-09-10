// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System.Collections.Generic;

namespace StandardIo.ArchitectureDiagram.SampleProject.Models;

public sealed class SchoolDataContext
{
    public List<School> Schools { get; } = new List<School>();

    public List<Teacher> Teachers { get; } = new List<Teacher>();

    public List<Student> Students { get; } = new List<Student>();

    public List<Class> Classes { get; } = new List<Class>();

    public List<ClassStudent> ClassStudents { get; } = new List<ClassStudent>();
}