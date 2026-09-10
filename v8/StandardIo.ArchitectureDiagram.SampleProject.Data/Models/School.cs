// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
namespace StandardIo.ArchitectureDiagram.SampleProject.Data.Models;
public sealed class School
{
    public string Id { get; set; } = string.Empty;
    public Teacher[] Teachers { get; set; } = [];
    public Student[] Students { get; set; } = [];
    public Class[] Classes { get; set; } = [];
}