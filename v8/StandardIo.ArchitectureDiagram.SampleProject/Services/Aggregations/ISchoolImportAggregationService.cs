// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Aggregations;
internal interface ISchoolImportAggregationService
{
    Task ImportSchool(School school);
}