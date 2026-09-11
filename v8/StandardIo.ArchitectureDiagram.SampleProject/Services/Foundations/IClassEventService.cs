// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;
internal interface IClassEventService
{
    ValueTask RaiseClassCreatedAsync(Class @class);

    ValueTask RaiseClassReadAsync(Class @class);

    ValueTask RaiseClassUpdatedAsync(Class @class);

    ValueTask RaiseClassDeletedAsync(Class @class);
}