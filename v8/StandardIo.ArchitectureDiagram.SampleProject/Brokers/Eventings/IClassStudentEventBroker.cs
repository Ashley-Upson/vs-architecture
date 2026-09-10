// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Brokers.Eventings;

public interface IClassStudentEventBroker
{
    ValueTask RaiseCreatedAsync(ClassStudent model);
    ValueTask RaiseReadAsync(ClassStudent model);
    ValueTask RaiseUpdatedAsync(ClassStudent model);
    ValueTask RaiseDeletedAsync(ClassStudent model);
}
