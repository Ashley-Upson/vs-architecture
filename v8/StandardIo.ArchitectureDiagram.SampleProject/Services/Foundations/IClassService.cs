// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using StandardIo.ArchitectureDiagram.SampleProject.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;

public interface IClassService
{
    Class Create(Class model);
    Class Read(string id);
    Class Update(Class model);
    void Delete(string id);
}
