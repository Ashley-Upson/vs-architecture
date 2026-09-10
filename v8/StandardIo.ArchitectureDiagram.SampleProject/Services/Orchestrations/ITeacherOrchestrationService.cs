// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Orchestrations;

public interface ITeacherOrchestrationService
{
    Task<Teacher> CreateAsync(Teacher model);
    Task<Teacher> ReadAsync(string id);
    Task<Teacher> UpdateAsync(Teacher model);
    Task DeleteAsync(string id);
}
