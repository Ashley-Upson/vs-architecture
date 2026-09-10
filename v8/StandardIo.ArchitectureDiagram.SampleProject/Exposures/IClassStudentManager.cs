// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Exposures;
public interface IClassStudentManager
{
    Task<ClassStudent> CreateClassStudentAsync(ClassStudent classStudent);

    Task<ClassStudent> ReadClassStudentAsync(string classStudentId);

    Task<ClassStudent> UpdateClassStudentAsync(ClassStudent updatedClassStudent);

    Task DeleteClassStudentAsync(string classStudentId);
}