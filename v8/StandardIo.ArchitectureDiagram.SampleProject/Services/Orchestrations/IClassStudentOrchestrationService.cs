// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Orchestrations;
internal interface IClassStudentOrchestrationService
{
    Task<ClassStudent> CreateClassStudentAsync(ClassStudent classStudent);

    Task<ClassStudent> ReadClassStudentAsync(string classStudentId);

    Task<ClassStudent> UpdateClassStudentAsync(ClassStudent updatedClassStudent);

    Task DeleteClassStudentAsync(string classStudentId);
}