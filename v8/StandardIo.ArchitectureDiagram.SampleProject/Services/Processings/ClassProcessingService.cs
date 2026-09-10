// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using StandardIo.ArchitectureDiagram.SampleProject.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Processings;

public sealed class ClassProcessingService : IClassProcessingService
{
    private readonly IClassService classService;

    public ClassProcessingService(IClassService classService)
    {
        this.classService = classService;
    }

    public Class Create(Class model) => classService.Create(model: model);

    public Class Read(string id) => classService.Read(id: id);

    public Class Update(Class model) => classService.Update(model: model);

    public void Delete(string id) => classService.Delete(id: id);
}
