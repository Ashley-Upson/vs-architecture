// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Services.Orchestrations;

namespace StandardIo.ArchitectureDiagram.SampleProject.Exposures;
public sealed class TeacherManager : ITeacherManager
{
    private readonly ITeacherOrchestrationService teacherOrchestrationService;
    internal TeacherManager(ITeacherOrchestrationService teacherOrchestrationService)
    {
        this.teacherOrchestrationService = teacherOrchestrationService;
    }

    public Task<Teacher> CreateTeacherAsync(Teacher teacher) =>
        teacherOrchestrationService.CreateTeacherAsync(teacher: teacher);

    public Task<Teacher> ReadTeacherAsync(string teacherId) =>
        teacherOrchestrationService.ReadTeacherAsync(teacherId: teacherId);

    public Task<Teacher> UpdateTeacherAsync(Teacher updatedTeacher) =>
        teacherOrchestrationService.UpdateTeacherAsync(updatedTeacher: updatedTeacher);

    public Task DeleteTeacherAsync(string teacherId) =>
        teacherOrchestrationService.DeleteTeacherAsync(teacherId: teacherId);
}