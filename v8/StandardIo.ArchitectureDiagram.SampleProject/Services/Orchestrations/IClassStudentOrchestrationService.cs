// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Orchestrations;

public interface IClassStudentOrchestrationService
{
    Task<ClassStudent> CreateAsync(ClassStudent model);
    Task<ClassStudent> ReadAsync(string id);
    Task<ClassStudent> UpdateAsync(ClassStudent model);
    Task DeleteAsync(string id);
}
