// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using StandardIo.ArchitectureDiagram.SampleProject.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Processings;

public interface ITeacherProcessingService
{
    Teacher Create(Teacher model);
    Teacher Read(string id);
    Teacher Update(Teacher model);
    void Delete(string id);
}
