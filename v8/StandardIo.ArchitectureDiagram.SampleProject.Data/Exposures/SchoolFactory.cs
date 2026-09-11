// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Data.Exposures;
public sealed class SchoolFactory : ISchoolFactory
{
    public SchoolDataContext CreateSchoolDataContext()
    {
        return new SchoolDataContext();
    }
}