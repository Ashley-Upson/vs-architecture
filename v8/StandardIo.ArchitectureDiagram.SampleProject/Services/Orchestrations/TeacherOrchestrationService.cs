// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Services.Processings;

namespace StandardIo.ArchitectureDiagram.SampleProject.Services.Orchestrations;
internal sealed class TeacherOrchestrationService : ITeacherOrchestrationService
{
    private readonly ITeacherProcessingService teacherProcessingService;
    private readonly ITeacherEventProcessingService teacherEventProcessingService;
    public TeacherOrchestrationService(ITeacherProcessingService teacherProcessingService, ITeacherEventProcessingService teacherEventProcessingService)
    {
        this.teacherProcessingService = teacherProcessingService;
        this.teacherEventProcessingService = teacherEventProcessingService;
    }

    public async Task<Teacher> CreateTeacherAsync(Teacher teacher)
    {
        Teacher result = teacherProcessingService.CreateTeacher(teacher: teacher);
        await teacherEventProcessingService.RaiseTeacherCreatedAsync(teacher: result);
        return result;
    }

    public async Task<Teacher> ReadTeacherAsync(string teacherId)
    {
        Teacher result = teacherProcessingService.ReadTeacher(teacherId: teacherId);
        await teacherEventProcessingService.RaiseTeacherReadAsync(teacher: result);
        return result;
    }

    public async Task<Teacher> UpdateTeacherAsync(Teacher updatedTeacher)
    {
        Teacher result = teacherProcessingService.UpdateTeacher(updatedTeacher: updatedTeacher);
        await teacherEventProcessingService.RaiseTeacherUpdatedAsync(teacher: result);
        return result;
    }

    public async Task DeleteTeacherAsync(string teacherId)
    {
        Teacher model = teacherProcessingService.ReadTeacher(teacherId: teacherId);
        teacherProcessingService.DeleteTeacher(teacherId: teacherId);
        await teacherEventProcessingService.RaiseTeacherDeletedAsync(teacher: model);
    }
}