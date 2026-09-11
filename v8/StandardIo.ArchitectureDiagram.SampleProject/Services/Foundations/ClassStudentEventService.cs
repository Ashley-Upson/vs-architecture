// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Brokers.Eventings;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;
internal sealed class ClassStudentEventService : IClassStudentEventService
{
    private readonly IClassStudentEventBroker classStudentEventBroker;
    public ClassStudentEventService(IClassStudentEventBroker classStudentEventBroker)
    {
        this.classStudentEventBroker = classStudentEventBroker;
    }

    public ValueTask RaiseClassStudentCreatedAsync(ClassStudent classStudent) =>
        classStudentEventBroker.RaiseClassStudentCreatedAsync(classStudent: classStudent);

    public ValueTask RaiseClassStudentReadAsync(ClassStudent classStudent) =>
        classStudentEventBroker.RaiseClassStudentReadAsync(classStudent: classStudent);

    public ValueTask RaiseClassStudentUpdatedAsync(ClassStudent classStudent) =>
        classStudentEventBroker.RaiseClassStudentUpdatedAsync(classStudent: classStudent);

    public ValueTask RaiseClassStudentDeletedAsync(ClassStudent classStudent) =>
        classStudentEventBroker.RaiseClassStudentDeletedAsync(classStudent: classStudent);
}