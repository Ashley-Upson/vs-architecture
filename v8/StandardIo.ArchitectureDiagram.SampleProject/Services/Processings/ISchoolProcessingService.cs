// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using StandardIo.ArchitectureDiagram.SampleProject.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Processings;

public interface ISchoolProcessingService
{
    School Create(School model);
    School Read(string id);
    School Update(School model);
    void Delete(string id);
}
