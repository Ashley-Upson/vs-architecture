// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Exposures;
public interface IClassManager
{
    Task<Class> CreateClassAsync(Class @class);

    Task<Class> ReadClassAsync(string classId);

    Task<Class> UpdateClassAsync(Class updatedClass);

    Task DeleteClassAsync(string classId);
}