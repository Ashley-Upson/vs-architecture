// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
namespace StandardIo.ArchitectureDiagram.SampleProject.Data.Models;
public sealed class Class
{
    public string Id { get; set; } = string.Empty;
    public string SchoolId { get; set; } = string.Empty;
    public string TeacherId { get; set; } = string.Empty;
    public Teacher? Teacher { get; set; }
    public ClassStudent[] Students { get; set; } = [];
}