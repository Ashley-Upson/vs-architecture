// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using StandardIo.ArchitectureDiagram.SampleProject.Brokers.Storages;
namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Processings;
public sealed class SchoolReportProcessingService(ISchoolReportBroker schoolReportBroker) : ISchoolReportProcessingService
{
    public void CreateReport()
    {
        schoolReportBroker.Read();
    }
}
