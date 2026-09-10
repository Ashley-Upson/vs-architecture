// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;
internal interface IClassStudentEventService
{
    ValueTask RaiseClassStudentCreatedAsync(ClassStudent classStudent);

    ValueTask RaiseClassStudentReadAsync(ClassStudent classStudent);

    ValueTask RaiseClassStudentUpdatedAsync(ClassStudent classStudent);

    ValueTask RaiseClassStudentDeletedAsync(ClassStudent classStudent);
}