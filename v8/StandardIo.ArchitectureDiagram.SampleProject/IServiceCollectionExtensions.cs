// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using cCoder.Eventing;
using StandardIo.ArchitectureDiagram.SampleProject.Services.Aggregations;
using Microsoft.Extensions.DependencyInjection;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;
using StandardIo.ArchitectureDiagram.SampleProject.Exposures;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Exposures;
using StandardIo.ArchitectureDiagram.SampleProject.Brokers.Storages;
using StandardIo.ArchitectureDiagram.SampleProject.Brokers.Eventings;
using StandardIo.ArchitectureDiagram.SampleProject.Services.Foundations;
using StandardIo.ArchitectureDiagram.SampleProject.Services.Processings;
using StandardIo.ArchitectureDiagram.SampleProject.Services.Orchestrations;

namespace StandardIo.ArchitectureDiagram.SampleProject;
public static class IServiceCollectionExtensions
{
    public static IServiceCollection AddSchoolSample(this IServiceCollection services)
    {
        services.AddLogging();
        services.AddEventing();
        services.AddSingleton<ISchoolFactory, SchoolFactory>();
        services.AddEventingForType<School>();
        services.AddSingleton<ISchoolBroker, SchoolBroker>();
        services.AddSingleton<ISchoolService, SchoolService>();
        services.AddSingleton<ISchoolProcessingService, SchoolProcessingService>();
        services.AddSingleton<ISchoolEventBroker, SchoolEventBroker>();
        services.AddSingleton<ISchoolEventService, SchoolEventService>();
        services.AddSingleton<ISchoolEventProcessingService, SchoolEventProcessingService>();
        services.AddSingleton<ISchoolOrchestrationService, SchoolOrchestrationService>();
        services.AddSingleton<ISchoolManager>(implementationFactory: provider => new SchoolManager(provider.GetRequiredService<ISchoolOrchestrationService>()));
        services.AddEventingForType<Teacher>();
        services.AddSingleton<ITeacherBroker, TeacherBroker>();
        services.AddSingleton<ITeacherService, TeacherService>();
        services.AddSingleton<ITeacherProcessingService, TeacherProcessingService>();
        services.AddSingleton<ITeacherEventBroker, TeacherEventBroker>();
        services.AddSingleton<ITeacherEventService, TeacherEventService>();
        services.AddSingleton<ITeacherEventProcessingService, TeacherEventProcessingService>();
        services.AddSingleton<ITeacherOrchestrationService, TeacherOrchestrationService>();
        services.AddSingleton<ITeacherManager>(implementationFactory: provider => new TeacherManager(provider.GetRequiredService<ITeacherOrchestrationService>()));
        services.AddEventingForType<Student>();
        services.AddSingleton<IStudentBroker, StudentBroker>();
        services.AddSingleton<IStudentService, StudentService>();
        services.AddSingleton<IStudentProcessingService, StudentProcessingService>();
        services.AddSingleton<IStudentEventBroker, StudentEventBroker>();
        services.AddSingleton<IStudentEventService, StudentEventService>();
        services.AddSingleton<IStudentEventProcessingService, StudentEventProcessingService>();
        services.AddSingleton<IStudentOrchestrationService, StudentOrchestrationService>();
        services.AddSingleton<IStudentManager>(implementationFactory: provider => new StudentManager(provider.GetRequiredService<IStudentOrchestrationService>()));
        services.AddEventingForType<Class>();
        services.AddSingleton<IClassBroker, ClassBroker>();
        services.AddSingleton<IClassService, ClassService>();
        services.AddSingleton<IClassProcessingService, ClassProcessingService>();
        services.AddSingleton<IClassEventBroker, ClassEventBroker>();
        services.AddSingleton<IClassEventService, ClassEventService>();
        services.AddSingleton<IClassEventProcessingService, ClassEventProcessingService>();
        services.AddSingleton<IClassOrchestrationService, ClassOrchestrationService>();
        services.AddSingleton<IClassManager>(implementationFactory: provider => new ClassManager(provider.GetRequiredService<IClassOrchestrationService>()));
        services.AddEventingForType<ClassStudent>();
        services.AddSingleton<IClassStudentBroker, ClassStudentBroker>();
        services.AddSingleton<IClassStudentService, ClassStudentService>();
        services.AddSingleton<IClassStudentProcessingService, ClassStudentProcessingService>();
        services.AddSingleton<IClassStudentEventBroker, ClassStudentEventBroker>();
        services.AddSingleton<IClassStudentEventService, ClassStudentEventService>();
        services.AddSingleton<IClassStudentEventProcessingService, ClassStudentEventProcessingService>();
        services.AddSingleton<IClassStudentOrchestrationService, ClassStudentOrchestrationService>();
        services.AddSingleton<IClassStudentManager>(implementationFactory: provider => new ClassStudentManager(provider.GetRequiredService<IClassStudentOrchestrationService>()));
        services.AddSingleton<ISchoolImportAggregationService, SchoolImportAggregationService>();
        services.AddSingleton<ISchoolImportManager>(implementationFactory: provider => new SchoolImportManager(provider.GetRequiredService<ISchoolImportAggregationService>()));
        return services;
    }
}