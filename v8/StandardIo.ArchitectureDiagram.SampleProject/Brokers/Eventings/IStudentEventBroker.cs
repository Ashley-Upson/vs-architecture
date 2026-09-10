// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Brokers.Eventings;

public interface IStudentEventBroker
{
    ValueTask RaiseCreatedAsync(Student model);
    ValueTask RaiseReadAsync(Student model);
    ValueTask RaiseUpdatedAsync(Student model);
    ValueTask RaiseDeletedAsync(Student model);
}
