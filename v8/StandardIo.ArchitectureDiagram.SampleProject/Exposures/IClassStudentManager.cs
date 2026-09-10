// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Exposures;

public interface IClassStudentManager
{
    Task<ClassStudent> CreateAsync(ClassStudent model);
    Task<ClassStudent> ReadAsync(string id);
    Task<ClassStudent> UpdateAsync(ClassStudent model);
    Task DeleteAsync(string id);
}
