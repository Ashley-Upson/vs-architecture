// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using StandardIo.ArchitectureDiagram.SampleProject.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Brokers.Storages;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;

public sealed class StudentService : IStudentService
{
    private readonly IStudentBroker studentBroker;

    public StudentService(IStudentBroker studentBroker)
    {
        this.studentBroker = studentBroker;
    }

    public Student Create(Student model) => studentBroker.Create(model: model);

    public Student Read(string id) => studentBroker.Read(id: id);

    public Student Update(Student model) => studentBroker.Update(model: model);

    public void Delete(string id) => studentBroker.Delete(id: id);
}
