// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using StandardIo.ArchitectureDiagram.SampleProject.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Exposures;

public sealed class SchoolFactory : ISchoolFactory
{
    public SchoolDataContext Create()
    {
        return new SchoolDataContext();
    }
}