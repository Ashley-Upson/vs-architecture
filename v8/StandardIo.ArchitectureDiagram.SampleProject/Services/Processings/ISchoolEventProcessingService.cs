// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Processings;
internal interface ISchoolEventProcessingService
{
    ValueTask RaiseSchoolCreatedAsync(School school);

    ValueTask RaiseSchoolReadAsync(School school);

    ValueTask RaiseSchoolUpdatedAsync(School school);

    ValueTask RaiseSchoolDeletedAsync(School school);
}