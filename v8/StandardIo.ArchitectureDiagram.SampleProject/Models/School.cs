// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

namespace StandardIo.ArchitectureDiagram.SampleProject.Models;

public sealed class School
{
    public string Id { get; set; } = string.Empty;

    public Teacher[] Teachers { get; set; } = new Teacher[0];

    public Student[] Students { get; set; } = new Student[0];

    public Class[] Classes { get; set; } = new Class[0];
}