// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using cCoder.Eventing;
using Microsoft.Extensions.DependencyInjection;
using StandardIo.ArchitectureDiagram.SampleProject;
using StandardIo.ArchitectureDiagram.SampleProject.Exposures;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Exposures;
using StandardIo.ArchitectureDiagram.SampleProject.Data.Models;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public sealed partial class SampleProjectBehaviorTests
{
    [Fact]
    public async Task ShouldImportSchoolAndEveryChildThroughTheOrchestrationStack()
    {
        using ServiceProvider provider = CreateProvider();
        var school = new School
        {
            Id = "school",
            Teachers = new[] { new Teacher { Id = "teacher1" }, new Teacher { Id = "teacher2" } },
            Students = new[] { new Student { Id = "student1" }, new Student { Id = "student2" } },
            Classes = new[]
            {
                new Class { Id = "class1", Students = new[] { new ClassStudent { Id = "enrolment1" }, new ClassStudent { Id = "enrolment2" } } },
                new Class { Id = "class2", Students = new[] { new ClassStudent { Id = "enrolment3" } } }
            }
        };
        await provider.GetRequiredService<ISchoolImportManager>().ImportSchool(school);
        Assert.Same(school, await provider.GetRequiredService<ISchoolManager>().ReadSchoolAsync(school.Id));
        foreach (var teacher in school.Teachers)
            Assert.Same(teacher, await provider.GetRequiredService<ITeacherManager>().ReadTeacherAsync(teacher.Id));
        foreach (var student in school.Students)
            Assert.Same(student, await provider.GetRequiredService<IStudentManager>().ReadStudentAsync(student.Id));
        foreach (var item in school.Classes)
        {
            Assert.Same(item, await provider.GetRequiredService<IClassManager>().ReadClassAsync(item.Id));
            foreach (var enrolment in item.Students)
                Assert.Same(enrolment, await provider.GetRequiredService<IClassStudentManager>().ReadClassStudentAsync(enrolment.Id));
        }
    }

    [Fact]
    public void ShouldInjectEverySampleDependencyThroughAnInterface()
    {
        // Given: the fixture and inputs below.
        // When: exercise the operation under test.
        var dependencies = typeof(SchoolManager).Assembly.GetTypes()
            .Where(predicate: type => type.IsClass)
            .SelectMany(selector: type => type.GetConstructors())
            .SelectMany(selector: constructor => constructor.GetParameters())
            .ToArray();
        // Then: verify the resulting contract.

        Assert.NotEmpty(collection: dependencies);
        Assert.All(collection: dependencies, action: dependency => Assert.True(condition: dependency.ParameterType.IsInterface, userMessage: $"{dependency.Member.DeclaringType!.Name}.{dependency.Name} must use an interface."));
    }

    [Fact]
    public void ShouldCreateIndependentEmptyDataContexts()
    {
        // Given: the fixture and inputs below.
        SchoolFactory factory = new SchoolFactory();
        // When: exercise the operation under test.
        SchoolDataContext first = factory.CreateSchoolDataContext();
        SchoolDataContext second = factory.CreateSchoolDataContext();
        first.Schools.Add(item: new School { Id = "first" });
        // Then: verify the resulting contract.
        Assert.NotSame(expected: first, actual: second);
        Assert.Empty(collection: second.Schools);
        Assert.Empty(collection: second.Teachers);
        Assert.Empty(collection: second.Students);
        Assert.Empty(collection: second.Classes);
        Assert.Empty(collection: second.ClassStudents);
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddSchoolSample();
        services.AddSingleton<EventRecorder<School>>();
        services.AddSingleton<EventRecorder<Teacher>>();
        services.AddSingleton<EventRecorder<Student>>();
        services.AddSingleton<EventRecorder<Class>>();
        services.AddSingleton<EventRecorder<ClassStudent>>();
        return services.BuildServiceProvider(options: new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    public sealed class EventRecorder<T>
    {
        public List<(string Operation, T Model)> Events { get; } = new();

        public ValueTask RecordAsync(string operation, T model)
        {
            Events.Add(item: (operation, model));
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task ShouldPersistSchoolCrudAndDeliverEventsThroughTheRealHubAsync()
    {
        // Given: the fixture and inputs below.
        using ServiceProvider provider = CreateProvider();
        ISchoolManager manager = provider.GetRequiredService<ISchoolManager>();
        IEventHub hub = provider.GetRequiredService<IEventHub>();
        EventRecorder<School> recorder = provider.GetRequiredService<EventRecorder<School>>();
        hub.ListenToEvent<School, EventRecorder<School>>(name: "School.Created", handler: (listener, model) => listener.RecordAsync(operation: "Created", model: model));
        hub.ListenToEvent<School, EventRecorder<School>>(name: "School.Read", handler: (listener, model) => listener.RecordAsync(operation: "Read", model: model));
        hub.ListenToEvent<School, EventRecorder<School>>(name: "School.Updated", handler: (listener, model) => listener.RecordAsync(operation: "Updated", model: model));
        hub.ListenToEvent<School, EventRecorder<School>>(name: "School.Deleted", handler: (listener, model) => listener.RecordAsync(operation: "Deleted", model: model));

        var original = new School
        {
            Id = "item"
        };

        var updated = new School
        {
            Id = "item"
        };
        // When: exercise the operation under test.

        Assert.Same(expected: original, actual: await manager.CreateSchoolAsync(school: original));
        // Then: verify the resulting contract.
        Assert.Same(expected: original, actual: await manager.ReadSchoolAsync(schoolId: "item"));
        Assert.Same(expected: updated, actual: await manager.UpdateSchoolAsync(updatedSchool: updated));
        await manager.DeleteSchoolAsync(schoolId: "item");

        Assert.Collection(collection: recorder.Events, elementInspectors: [item =>
        {
            Assert.Equal(expected: "Created", actual: item.Operation);
            Assert.Same(expected: original, actual: item.Model);
        }, item =>
        {
            Assert.Equal(expected: "Read", actual: item.Operation);
            Assert.Same(expected: original, actual: item.Model);
        }, item =>
        {
            Assert.Equal(expected: "Updated", actual: item.Operation);
            Assert.Same(expected: updated, actual: item.Model);
        }, item =>
        {
            Assert.Equal(expected: "Deleted", actual: item.Operation);
            Assert.Same(expected: updated, actual: item.Model);
        }]);

        await Assert.ThrowsAsync<InvalidOperationException>(testCode: () => manager.ReadSchoolAsync(schoolId: "item"));
        await Assert.ThrowsAsync<InvalidOperationException>(testCode: () => manager.UpdateSchoolAsync(updatedSchool: updated));
        await Assert.ThrowsAsync<InvalidOperationException>(testCode: () => manager.DeleteSchoolAsync(schoolId: "item"));
        Assert.Equal(expected: 4, actual: recorder.Events.Count);
    }

    [Fact]
    public async Task ShouldPersistTeacherCrudAndDeliverEventsThroughTheRealHubAsync()
    {
        // Given: the fixture and inputs below.
        using ServiceProvider provider = CreateProvider();
        ITeacherManager manager = provider.GetRequiredService<ITeacherManager>();
        IEventHub hub = provider.GetRequiredService<IEventHub>();
        EventRecorder<Teacher> recorder = provider.GetRequiredService<EventRecorder<Teacher>>();
        hub.ListenToEvent<Teacher, EventRecorder<Teacher>>(name: "Teacher.Created", handler: (listener, model) => listener.RecordAsync(operation: "Created", model: model));
        hub.ListenToEvent<Teacher, EventRecorder<Teacher>>(name: "Teacher.Read", handler: (listener, model) => listener.RecordAsync(operation: "Read", model: model));
        hub.ListenToEvent<Teacher, EventRecorder<Teacher>>(name: "Teacher.Updated", handler: (listener, model) => listener.RecordAsync(operation: "Updated", model: model));
        hub.ListenToEvent<Teacher, EventRecorder<Teacher>>(name: "Teacher.Deleted", handler: (listener, model) => listener.RecordAsync(operation: "Deleted", model: model));

        var original = new Teacher
        {
            Id = "item"
        };

        var updated = new Teacher
        {
            Id = "item"
        };
        // When: exercise the operation under test.

        Assert.Same(expected: original, actual: await manager.CreateTeacherAsync(teacher: original));
        // Then: verify the resulting contract.
        Assert.Same(expected: original, actual: await manager.ReadTeacherAsync(teacherId: "item"));
        Assert.Same(expected: updated, actual: await manager.UpdateTeacherAsync(updatedTeacher: updated));
        await manager.DeleteTeacherAsync(teacherId: "item");

        Assert.Collection(collection: recorder.Events, elementInspectors: [item =>
        {
            Assert.Equal(expected: "Created", actual: item.Operation);
            Assert.Same(expected: original, actual: item.Model);
        }, item =>
        {
            Assert.Equal(expected: "Read", actual: item.Operation);
            Assert.Same(expected: original, actual: item.Model);
        }, item =>
        {
            Assert.Equal(expected: "Updated", actual: item.Operation);
            Assert.Same(expected: updated, actual: item.Model);
        }, item =>
        {
            Assert.Equal(expected: "Deleted", actual: item.Operation);
            Assert.Same(expected: updated, actual: item.Model);
        }]);

        await Assert.ThrowsAsync<InvalidOperationException>(testCode: () => manager.ReadTeacherAsync(teacherId: "item"));
        await Assert.ThrowsAsync<InvalidOperationException>(testCode: () => manager.UpdateTeacherAsync(updatedTeacher: updated));
        await Assert.ThrowsAsync<InvalidOperationException>(testCode: () => manager.DeleteTeacherAsync(teacherId: "item"));
        Assert.Equal(expected: 4, actual: recorder.Events.Count);
    }

    [Fact]
    public async Task ShouldPersistStudentCrudAndDeliverEventsThroughTheRealHubAsync()
    {
        // Given: the fixture and inputs below.
        using ServiceProvider provider = CreateProvider();
        IStudentManager manager = provider.GetRequiredService<IStudentManager>();
        IEventHub hub = provider.GetRequiredService<IEventHub>();
        EventRecorder<Student> recorder = provider.GetRequiredService<EventRecorder<Student>>();
        hub.ListenToEvent<Student, EventRecorder<Student>>(name: "Student.Created", handler: (listener, model) => listener.RecordAsync(operation: "Created", model: model));
        hub.ListenToEvent<Student, EventRecorder<Student>>(name: "Student.Read", handler: (listener, model) => listener.RecordAsync(operation: "Read", model: model));
        hub.ListenToEvent<Student, EventRecorder<Student>>(name: "Student.Updated", handler: (listener, model) => listener.RecordAsync(operation: "Updated", model: model));
        hub.ListenToEvent<Student, EventRecorder<Student>>(name: "Student.Deleted", handler: (listener, model) => listener.RecordAsync(operation: "Deleted", model: model));

        var original = new Student
        {
            Id = "item"
        };

        var updated = new Student
        {
            Id = "item"
        };
        // When: exercise the operation under test.

        Assert.Same(expected: original, actual: await manager.CreateStudentAsync(student: original));
        // Then: verify the resulting contract.
        Assert.Same(expected: original, actual: await manager.ReadStudentAsync(studentId: "item"));
        Assert.Same(expected: updated, actual: await manager.UpdateStudentAsync(updatedStudent: updated));
        await manager.DeleteStudentAsync(studentId: "item");

        Assert.Collection(collection: recorder.Events, elementInspectors: [item =>
        {
            Assert.Equal(expected: "Created", actual: item.Operation);
            Assert.Same(expected: original, actual: item.Model);
        }, item =>
        {
            Assert.Equal(expected: "Read", actual: item.Operation);
            Assert.Same(expected: original, actual: item.Model);
        }, item =>
        {
            Assert.Equal(expected: "Updated", actual: item.Operation);
            Assert.Same(expected: updated, actual: item.Model);
        }, item =>
        {
            Assert.Equal(expected: "Deleted", actual: item.Operation);
            Assert.Same(expected: updated, actual: item.Model);
        }]);

        await Assert.ThrowsAsync<InvalidOperationException>(testCode: () => manager.ReadStudentAsync(studentId: "item"));
        await Assert.ThrowsAsync<InvalidOperationException>(testCode: () => manager.UpdateStudentAsync(updatedStudent: updated));
        await Assert.ThrowsAsync<InvalidOperationException>(testCode: () => manager.DeleteStudentAsync(studentId: "item"));
        Assert.Equal(expected: 4, actual: recorder.Events.Count);
    }

    [Fact]
    public async Task ShouldPersistClassCrudAndDeliverEventsThroughTheRealHubAsync()
    {
        // Given: the fixture and inputs below.
        using ServiceProvider provider = CreateProvider();
        IClassManager manager = provider.GetRequiredService<IClassManager>();
        IEventHub hub = provider.GetRequiredService<IEventHub>();
        EventRecorder<Class> recorder = provider.GetRequiredService<EventRecorder<Class>>();
        hub.ListenToEvent<Class, EventRecorder<Class>>(name: "Class.Created", handler: (listener, model) => listener.RecordAsync(operation: "Created", model: model));
        hub.ListenToEvent<Class, EventRecorder<Class>>(name: "Class.Read", handler: (listener, model) => listener.RecordAsync(operation: "Read", model: model));
        hub.ListenToEvent<Class, EventRecorder<Class>>(name: "Class.Updated", handler: (listener, model) => listener.RecordAsync(operation: "Updated", model: model));
        hub.ListenToEvent<Class, EventRecorder<Class>>(name: "Class.Deleted", handler: (listener, model) => listener.RecordAsync(operation: "Deleted", model: model));

        var original = new Class
        {
            Id = "item"
        };

        var updated = new Class
        {
            Id = "item"
        };
        // When: exercise the operation under test.

        Assert.Same(expected: original, actual: await manager.CreateClassAsync(@class: original));
        // Then: verify the resulting contract.
        Assert.Same(expected: original, actual: await manager.ReadClassAsync(classId: "item"));
        Assert.Same(expected: updated, actual: await manager.UpdateClassAsync(updatedClass: updated));
        await manager.DeleteClassAsync(classId: "item");

        Assert.Collection(collection: recorder.Events, elementInspectors: [item =>
        {
            Assert.Equal(expected: "Created", actual: item.Operation);
            Assert.Same(expected: original, actual: item.Model);
        }, item =>
        {
            Assert.Equal(expected: "Read", actual: item.Operation);
            Assert.Same(expected: original, actual: item.Model);
        }, item =>
        {
            Assert.Equal(expected: "Updated", actual: item.Operation);
            Assert.Same(expected: updated, actual: item.Model);
        }, item =>
        {
            Assert.Equal(expected: "Deleted", actual: item.Operation);
            Assert.Same(expected: updated, actual: item.Model);
        }]);

        await Assert.ThrowsAsync<InvalidOperationException>(testCode: () => manager.ReadClassAsync(classId: "item"));
        await Assert.ThrowsAsync<InvalidOperationException>(testCode: () => manager.UpdateClassAsync(updatedClass: updated));
        await Assert.ThrowsAsync<InvalidOperationException>(testCode: () => manager.DeleteClassAsync(classId: "item"));
        Assert.Equal(expected: 4, actual: recorder.Events.Count);
    }

    [Fact]
    public async Task ShouldPersistClassStudentCrudAndDeliverEventsThroughTheRealHubAsync()
    {
        // Given: the fixture and inputs below.
        using ServiceProvider provider = CreateProvider();
        IClassStudentManager manager = provider.GetRequiredService<IClassStudentManager>();
        IEventHub hub = provider.GetRequiredService<IEventHub>();
        EventRecorder<ClassStudent> recorder = provider.GetRequiredService<EventRecorder<ClassStudent>>();
        hub.ListenToEvent<ClassStudent, EventRecorder<ClassStudent>>(name: "ClassStudent.Created", handler: (listener, model) => listener.RecordAsync(operation: "Created", model: model));
        hub.ListenToEvent<ClassStudent, EventRecorder<ClassStudent>>(name: "ClassStudent.Read", handler: (listener, model) => listener.RecordAsync(operation: "Read", model: model));
        hub.ListenToEvent<ClassStudent, EventRecorder<ClassStudent>>(name: "ClassStudent.Updated", handler: (listener, model) => listener.RecordAsync(operation: "Updated", model: model));
        hub.ListenToEvent<ClassStudent, EventRecorder<ClassStudent>>(name: "ClassStudent.Deleted", handler: (listener, model) => listener.RecordAsync(operation: "Deleted", model: model));

        var original = new ClassStudent
        {
            Id = "item"
        };

        var updated = new ClassStudent
        {
            Id = "item"
        };
        // When: exercise the operation under test.

        Assert.Same(expected: original, actual: await manager.CreateClassStudentAsync(classStudent: original));
        // Then: verify the resulting contract.
        Assert.Same(expected: original, actual: await manager.ReadClassStudentAsync(classStudentId: "item"));
        Assert.Same(expected: updated, actual: await manager.UpdateClassStudentAsync(updatedClassStudent: updated));
        await manager.DeleteClassStudentAsync(classStudentId: "item");

        Assert.Collection(collection: recorder.Events, elementInspectors: [item =>
        {
            Assert.Equal(expected: "Created", actual: item.Operation);
            Assert.Same(expected: original, actual: item.Model);
        }, item =>
        {
            Assert.Equal(expected: "Read", actual: item.Operation);
            Assert.Same(expected: original, actual: item.Model);
        }, item =>
        {
            Assert.Equal(expected: "Updated", actual: item.Operation);
            Assert.Same(expected: updated, actual: item.Model);
        }, item =>
        {
            Assert.Equal(expected: "Deleted", actual: item.Operation);
            Assert.Same(expected: updated, actual: item.Model);
        }]);

        await Assert.ThrowsAsync<InvalidOperationException>(testCode: () => manager.ReadClassStudentAsync(classStudentId: "item"));
        await Assert.ThrowsAsync<InvalidOperationException>(testCode: () => manager.UpdateClassStudentAsync(updatedClassStudent: updated));
        await Assert.ThrowsAsync<InvalidOperationException>(testCode: () => manager.DeleteClassStudentAsync(classStudentId: "item"));
        Assert.Equal(expected: 4, actual: recorder.Events.Count);
    }

    [Fact]
    public async Task ShouldKeepSchoolCrudIndependentOfChildStacksAsync()
    {
        // Given: the fixture and inputs below.
        using ServiceProvider provider = CreateProvider();
        ISchoolManager schools = provider.GetRequiredService<ISchoolManager>();
        ITeacherManager teachers = provider.GetRequiredService<ITeacherManager>();

        var teacher = new Teacher
        {
            Id = "teacher",
            SchoolId = "school"
        };

        var school = new School
        {
            Id = "school",
            Teachers = new[]
            {
                teacher
            }
        };
        // When: exercise the operation under test.

        await schools.CreateSchoolAsync(school: school);
        // Then: verify the resulting contract.
        await Assert.ThrowsAsync<InvalidOperationException>(testCode: () => teachers.ReadTeacherAsync(teacherId: "teacher"));
        await teachers.CreateTeacherAsync(teacher: teacher);
        await schools.DeleteSchoolAsync(schoolId: "school");
        Assert.Same(expected: teacher, actual: await teachers.ReadTeacherAsync(teacherId: "teacher"));
    }
}