// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Processings;

public interface ITeacherEventProcessingService
{
    ValueTask RaiseCreatedAsync(Teacher model);
    ValueTask RaiseReadAsync(Teacher model);
    ValueTask RaiseUpdatedAsync(Teacher model);
    ValueTask RaiseDeletedAsync(Teacher model);
}
