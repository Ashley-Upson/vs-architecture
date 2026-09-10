// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Orchestrations;

public interface IClassOrchestrationService
{
    Task<Class> CreateAsync(Class model);
    Task<Class> ReadAsync(string id);
    Task<Class> UpdateAsync(Class model);
    Task DeleteAsync(string id);
}
