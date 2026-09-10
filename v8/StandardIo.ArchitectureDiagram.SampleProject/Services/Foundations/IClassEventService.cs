// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;

public interface IClassEventService
{
    ValueTask RaiseCreatedAsync(Class model);
    ValueTask RaiseReadAsync(Class model);
    ValueTask RaiseUpdatedAsync(Class model);
    ValueTask RaiseDeletedAsync(Class model);
}
