// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using StandardIo.ArchitectureDiagram.SampleProject.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Processings;

public sealed class SchoolProcessingService : ISchoolProcessingService
{
    private readonly ISchoolService schoolService;

    public SchoolProcessingService(ISchoolService schoolService)
    {
        this.schoolService = schoolService;
    }

    public School Create(School model) => schoolService.Create(model: model);

    public School Read(string id) => schoolService.Read(id: id);

    public School Update(School model) => schoolService.Update(model: model);

    public void Delete(string id) => schoolService.Delete(id: id);
}
