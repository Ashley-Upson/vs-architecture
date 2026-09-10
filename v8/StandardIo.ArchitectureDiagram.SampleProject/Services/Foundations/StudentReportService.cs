// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using StandardIo.ArchitectureDiagram.SampleProject.Brokers.Storages;
namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;
public sealed class StudentReportService(IStudentReportBroker studentReportBroker) : IStudentReportService
{
    public void Read()
    {
        studentReportBroker.Read();
    }
}
