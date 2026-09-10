// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Brokers.Eventings;
internal interface IStudentEventBroker
{
    ValueTask RaiseStudentCreatedAsync(Student student);

    ValueTask RaiseStudentReadAsync(Student student);

    ValueTask RaiseStudentUpdatedAsync(Student student);

    ValueTask RaiseStudentDeletedAsync(Student student);
}