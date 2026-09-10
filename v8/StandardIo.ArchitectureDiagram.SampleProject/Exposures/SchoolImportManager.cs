// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Services.Aggregations;

namespace StandardIo.ArchitectureDiagram.SampleProject.Exposures;
public sealed class SchoolImportManager : ISchoolImportManager
{
    private readonly ISchoolImportAggregationService schoolImportAggregationService;
    internal SchoolImportManager(ISchoolImportAggregationService schoolImportAggregationService)
    {
        this.schoolImportAggregationService = schoolImportAggregationService;
    }

    public Task ImportSchool(School school) =>
        schoolImportAggregationService.ImportSchool(school: school);
}