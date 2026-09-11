// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Brokers.Eventings;
internal interface IClassEventBroker
{
    ValueTask RaiseClassCreatedAsync(Class @class);

    ValueTask RaiseClassReadAsync(Class @class);

    ValueTask RaiseClassUpdatedAsync(Class @class);

    ValueTask RaiseClassDeletedAsync(Class @class);
}