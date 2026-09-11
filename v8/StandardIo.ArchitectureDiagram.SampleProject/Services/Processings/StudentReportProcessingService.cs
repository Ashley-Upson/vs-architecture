// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using StandardIo.ArchitectureDiagram.SampleProject.Brokers.Storages;
using StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;
namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Processings;
public sealed class StudentReportProcessingService(ISchoolReportBroker schoolReportBroker, IStudentReportService studentReportService) : IStudentReportProcessingService
{
    public void CreateReport()
    {
        schoolReportBroker.Read();
        studentReportService.Read();
    }
}
