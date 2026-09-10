// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;
internal interface ITeacherEventService
{
    ValueTask RaiseTeacherCreatedAsync(Teacher teacher);

    ValueTask RaiseTeacherReadAsync(Teacher teacher);

    ValueTask RaiseTeacherUpdatedAsync(Teacher teacher);

    ValueTask RaiseTeacherDeletedAsync(Teacher teacher);
}