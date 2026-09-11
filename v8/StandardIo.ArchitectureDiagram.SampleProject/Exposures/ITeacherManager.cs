// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Exposures;
public interface ITeacherManager
{
    Task<Teacher> CreateTeacherAsync(Teacher teacher);

    Task<Teacher> ReadTeacherAsync(string teacherId);

    Task<Teacher> UpdateTeacherAsync(Teacher updatedTeacher);

    Task DeleteTeacherAsync(string teacherId);
}