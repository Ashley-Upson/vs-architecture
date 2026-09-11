// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Services.Orchestrations;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Aggregations;
internal sealed class SchoolImportAggregationService : ISchoolImportAggregationService
{
    private readonly ISchoolOrchestrationService schoolOrchestrationService;
    private readonly ITeacherOrchestrationService teacherOrchestrationService;
    private readonly IStudentOrchestrationService studentOrchestrationService;
    private readonly IClassOrchestrationService classOrchestrationService;
    private readonly IClassStudentOrchestrationService classStudentOrchestrationService;
    public SchoolImportAggregationService(
        ISchoolOrchestrationService schoolOrchestrationService,
        ITeacherOrchestrationService teacherOrchestrationService,
        IStudentOrchestrationService studentOrchestrationService,
        IClassOrchestrationService classOrchestrationService,
        IClassStudentOrchestrationService classStudentOrchestrationService)
    {
        this.schoolOrchestrationService = schoolOrchestrationService;
        this.teacherOrchestrationService = teacherOrchestrationService;
        this.studentOrchestrationService = studentOrchestrationService;
        this.classOrchestrationService = classOrchestrationService;
        this.classStudentOrchestrationService = classStudentOrchestrationService;
    }

    public async Task ImportSchool(School school)
    {
        await schoolOrchestrationService.CreateSchoolAsync(school: school);

        foreach (Teacher teacher in school.Teachers)
        {
            await teacherOrchestrationService.CreateTeacherAsync(teacher: teacher);
        }

        foreach (Student student in school.Students)
        {
            await studentOrchestrationService.CreateStudentAsync(student: student);
        }

        foreach (Class @class in school.Classes)
        {
            await classOrchestrationService.CreateClassAsync(@class: @class);

            foreach (ClassStudent classStudent in @class.Students)
            {
                await classStudentOrchestrationService.CreateClassStudentAsync(classStudent: classStudent);
            }
        }
    }
}