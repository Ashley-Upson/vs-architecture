// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;

public interface ISchoolEventService
{
    ValueTask RaiseCreatedAsync(School model);
    ValueTask RaiseReadAsync(School model);
    ValueTask RaiseUpdatedAsync(School model);
    ValueTask RaiseDeletedAsync(School model);
}
