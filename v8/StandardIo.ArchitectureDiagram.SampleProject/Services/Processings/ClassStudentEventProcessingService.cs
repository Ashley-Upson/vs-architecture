// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Processings;
internal sealed class ClassStudentEventProcessingService : IClassStudentEventProcessingService
{
    private readonly IClassStudentEventService classStudentEventService;
    public ClassStudentEventProcessingService(IClassStudentEventService classStudentEventService)
    {
        this.classStudentEventService = classStudentEventService;
    }

    public ValueTask RaiseClassStudentCreatedAsync(ClassStudent classStudent) =>
        classStudentEventService.RaiseClassStudentCreatedAsync(classStudent: classStudent);

    public ValueTask RaiseClassStudentReadAsync(ClassStudent classStudent) =>
        classStudentEventService.RaiseClassStudentReadAsync(classStudent: classStudent);

    public ValueTask RaiseClassStudentUpdatedAsync(ClassStudent classStudent) =>
        classStudentEventService.RaiseClassStudentUpdatedAsync(classStudent: classStudent);

    public ValueTask RaiseClassStudentDeletedAsync(ClassStudent classStudent) =>
        classStudentEventService.RaiseClassStudentDeletedAsync(classStudent: classStudent);
}