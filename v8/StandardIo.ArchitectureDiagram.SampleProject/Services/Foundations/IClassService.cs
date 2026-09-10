// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;
internal interface IClassService
{
    Class CreateClass(Class @class);

    Class ReadClass(string classId);

    Class UpdateClass(Class updatedClass);

    void DeleteClass(string classId);
}