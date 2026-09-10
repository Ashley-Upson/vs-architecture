// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Orchestrations;

public interface ISchoolOrchestrationService
{
    Task<School> CreateAsync(School model);
    Task<School> ReadAsync(string id);
    Task<School> UpdateAsync(School model);
    Task DeleteAsync(string id);
}
