// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Processings;

public interface IStudentEventProcessingService
{
    ValueTask RaiseCreatedAsync(Student model);
    ValueTask RaiseReadAsync(Student model);
    ValueTask RaiseUpdatedAsync(Student model);
    ValueTask RaiseDeletedAsync(Student model);
}
