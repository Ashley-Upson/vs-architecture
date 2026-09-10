// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Exposures;
public interface ISchoolManager
{
    Task<School> CreateSchoolAsync(School school);

    Task<School> ReadSchoolAsync(string schoolId);

    Task<School> UpdateSchoolAsync(School updatedSchool);

    Task DeleteSchoolAsync(string schoolId);
}