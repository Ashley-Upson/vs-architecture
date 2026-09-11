// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using StandardIo.ArchitectureDiagram.SampleProject.Services.Processings;
namespace StandardIo.ArchitectureDiagram.SampleProject.Exposures;
public sealed class SchoolReportManager(ISchoolReportProcessingService schoolReportProcessingService, IStudentReportProcessingService studentReportProcessingService) : ISchoolReportManager
{
    public void CreateReport()
    {
        schoolReportProcessingService.CreateReport();
        studentReportProcessingService.CreateReport();
    }
}
