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
using StandardIo.ArchitectureDiagram.SampleProject.Models;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core2.Tests;

public sealed partial class SampleProjectBehaviorTests
{
    [Fact]
    public void ShouldInjectEverySampleDependencyThroughAnInterface()
    {
        var dependencies = typeof(SchoolManager).Assembly.GetTypes()
            .Where(type => type.IsClass)
            .SelectMany(type => type.GetConstructors())
            .SelectMany(constructor => constructor.GetParameters())
            .ToArray();

        Assert.NotEmpty(dependencies);
        Assert.All(dependencies, dependency => Assert.True(
            dependency.ParameterType.IsInterface,
            $"{dependency.Member.DeclaringType!.Name}.{dependency.Name} must use an interface."));
    }

    [Fact]
    public void ShouldCreateIndependentEmptyDataContexts()
    {
        ISchoolFactory factory = new SchoolFactory();
        SchoolDataContext first = factory.Create();
        SchoolDataContext second = factory.Create();
        first.Schools.Add(new School { Id = "first" });
        Assert.NotSame(first, second);
        Assert.Empty(second.Schools);
        Assert.Empty(second.Teachers);
        Assert.Empty(second.Students);
        Assert.Empty(second.Classes);
        Assert.Empty(second.ClassStudents);
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
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }

    public sealed class EventRecorder<T>
    {
        public List<(string Operation, T Model)> Events { get; } = new();

        public ValueTask RecordAsync(string operation, T model)
        {
            Events.Add((operation, model));
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task ShouldPersistSchoolCrudAndDeliverEventsThroughTheRealHubAsync()
    {
        using ServiceProvider provider = CreateProvider();
        ISchoolManager manager = provider.GetRequiredService<ISchoolManager>();
        IEventHub hub = provider.GetRequiredService<IEventHub>();
        EventRecorder<School> recorder = provider.GetRequiredService<EventRecorder<School>>();
        hub.ListenToEvent<School, EventRecorder<School>>("School.Created", (listener, model) => listener.RecordAsync("Created", model));
        hub.ListenToEvent<School, EventRecorder<School>>("School.Read", (listener, model) => listener.RecordAsync("Read", model));
        hub.ListenToEvent<School, EventRecorder<School>>("School.Updated", (listener, model) => listener.RecordAsync("Updated", model));
        hub.ListenToEvent<School, EventRecorder<School>>("School.Deleted", (listener, model) => listener.RecordAsync("Deleted", model));

        var original = new School { Id = "item" };
        var updated = new School { Id = "item" };
        Assert.Same(original, await manager.CreateAsync(original));
        Assert.Same(original, await manager.ReadAsync("item"));
        Assert.Same(updated, await manager.UpdateAsync(updated));
        await manager.DeleteAsync("item");

        Assert.Collection(recorder.Events,
            item => { Assert.Equal("Created", item.Operation); Assert.Same(original, item.Model); },
            item => { Assert.Equal("Read", item.Operation); Assert.Same(original, item.Model); },
            item => { Assert.Equal("Updated", item.Operation); Assert.Same(updated, item.Model); },
            item => { Assert.Equal("Deleted", item.Operation); Assert.Same(updated, item.Model); });

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.ReadAsync("item"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.UpdateAsync(updated));
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.DeleteAsync("item"));
        Assert.Equal(4, recorder.Events.Count);
    }

    [Fact]
    public async Task ShouldPersistTeacherCrudAndDeliverEventsThroughTheRealHubAsync()
    {
        using ServiceProvider provider = CreateProvider();
        ITeacherManager manager = provider.GetRequiredService<ITeacherManager>();
        IEventHub hub = provider.GetRequiredService<IEventHub>();
        EventRecorder<Teacher> recorder = provider.GetRequiredService<EventRecorder<Teacher>>();
        hub.ListenToEvent<Teacher, EventRecorder<Teacher>>("Teacher.Created", (listener, model) => listener.RecordAsync("Created", model));
        hub.ListenToEvent<Teacher, EventRecorder<Teacher>>("Teacher.Read", (listener, model) => listener.RecordAsync("Read", model));
        hub.ListenToEvent<Teacher, EventRecorder<Teacher>>("Teacher.Updated", (listener, model) => listener.RecordAsync("Updated", model));
        hub.ListenToEvent<Teacher, EventRecorder<Teacher>>("Teacher.Deleted", (listener, model) => listener.RecordAsync("Deleted", model));

        var original = new Teacher { Id = "item" };
        var updated = new Teacher { Id = "item" };
        Assert.Same(original, await manager.CreateAsync(original));
        Assert.Same(original, await manager.ReadAsync("item"));
        Assert.Same(updated, await manager.UpdateAsync(updated));
        await manager.DeleteAsync("item");

        Assert.Collection(recorder.Events,
            item => { Assert.Equal("Created", item.Operation); Assert.Same(original, item.Model); },
            item => { Assert.Equal("Read", item.Operation); Assert.Same(original, item.Model); },
            item => { Assert.Equal("Updated", item.Operation); Assert.Same(updated, item.Model); },
            item => { Assert.Equal("Deleted", item.Operation); Assert.Same(updated, item.Model); });

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.ReadAsync("item"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.UpdateAsync(updated));
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.DeleteAsync("item"));
        Assert.Equal(4, recorder.Events.Count);
    }

    [Fact]
    public async Task ShouldPersistStudentCrudAndDeliverEventsThroughTheRealHubAsync()
    {
        using ServiceProvider provider = CreateProvider();
        IStudentManager manager = provider.GetRequiredService<IStudentManager>();
        IEventHub hub = provider.GetRequiredService<IEventHub>();
        EventRecorder<Student> recorder = provider.GetRequiredService<EventRecorder<Student>>();
        hub.ListenToEvent<Student, EventRecorder<Student>>("Student.Created", (listener, model) => listener.RecordAsync("Created", model));
        hub.ListenToEvent<Student, EventRecorder<Student>>("Student.Read", (listener, model) => listener.RecordAsync("Read", model));
        hub.ListenToEvent<Student, EventRecorder<Student>>("Student.Updated", (listener, model) => listener.RecordAsync("Updated", model));
        hub.ListenToEvent<Student, EventRecorder<Student>>("Student.Deleted", (listener, model) => listener.RecordAsync("Deleted", model));

        var original = new Student { Id = "item" };
        var updated = new Student { Id = "item" };
        Assert.Same(original, await manager.CreateAsync(original));
        Assert.Same(original, await manager.ReadAsync("item"));
        Assert.Same(updated, await manager.UpdateAsync(updated));
        await manager.DeleteAsync("item");

        Assert.Collection(recorder.Events,
            item => { Assert.Equal("Created", item.Operation); Assert.Same(original, item.Model); },
            item => { Assert.Equal("Read", item.Operation); Assert.Same(original, item.Model); },
            item => { Assert.Equal("Updated", item.Operation); Assert.Same(updated, item.Model); },
            item => { Assert.Equal("Deleted", item.Operation); Assert.Same(updated, item.Model); });

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.ReadAsync("item"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.UpdateAsync(updated));
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.DeleteAsync("item"));
        Assert.Equal(4, recorder.Events.Count);
    }

    [Fact]
    public async Task ShouldPersistClassCrudAndDeliverEventsThroughTheRealHubAsync()
    {
        using ServiceProvider provider = CreateProvider();
        IClassManager manager = provider.GetRequiredService<IClassManager>();
        IEventHub hub = provider.GetRequiredService<IEventHub>();
        EventRecorder<Class> recorder = provider.GetRequiredService<EventRecorder<Class>>();
        hub.ListenToEvent<Class, EventRecorder<Class>>("Class.Created", (listener, model) => listener.RecordAsync("Created", model));
        hub.ListenToEvent<Class, EventRecorder<Class>>("Class.Read", (listener, model) => listener.RecordAsync("Read", model));
        hub.ListenToEvent<Class, EventRecorder<Class>>("Class.Updated", (listener, model) => listener.RecordAsync("Updated", model));
        hub.ListenToEvent<Class, EventRecorder<Class>>("Class.Deleted", (listener, model) => listener.RecordAsync("Deleted", model));

        var original = new Class { Id = "item" };
        var updated = new Class { Id = "item" };
        Assert.Same(original, await manager.CreateAsync(original));
        Assert.Same(original, await manager.ReadAsync("item"));
        Assert.Same(updated, await manager.UpdateAsync(updated));
        await manager.DeleteAsync("item");

        Assert.Collection(recorder.Events,
            item => { Assert.Equal("Created", item.Operation); Assert.Same(original, item.Model); },
            item => { Assert.Equal("Read", item.Operation); Assert.Same(original, item.Model); },
            item => { Assert.Equal("Updated", item.Operation); Assert.Same(updated, item.Model); },
            item => { Assert.Equal("Deleted", item.Operation); Assert.Same(updated, item.Model); });

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.ReadAsync("item"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.UpdateAsync(updated));
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.DeleteAsync("item"));
        Assert.Equal(4, recorder.Events.Count);
    }

    [Fact]
    public async Task ShouldPersistClassStudentCrudAndDeliverEventsThroughTheRealHubAsync()
    {
        using ServiceProvider provider = CreateProvider();
        IClassStudentManager manager = provider.GetRequiredService<IClassStudentManager>();
        IEventHub hub = provider.GetRequiredService<IEventHub>();
        EventRecorder<ClassStudent> recorder = provider.GetRequiredService<EventRecorder<ClassStudent>>();
        hub.ListenToEvent<ClassStudent, EventRecorder<ClassStudent>>("ClassStudent.Created", (listener, model) => listener.RecordAsync("Created", model));
        hub.ListenToEvent<ClassStudent, EventRecorder<ClassStudent>>("ClassStudent.Read", (listener, model) => listener.RecordAsync("Read", model));
        hub.ListenToEvent<ClassStudent, EventRecorder<ClassStudent>>("ClassStudent.Updated", (listener, model) => listener.RecordAsync("Updated", model));
        hub.ListenToEvent<ClassStudent, EventRecorder<ClassStudent>>("ClassStudent.Deleted", (listener, model) => listener.RecordAsync("Deleted", model));

        var original = new ClassStudent { Id = "item" };
        var updated = new ClassStudent { Id = "item" };
        Assert.Same(original, await manager.CreateAsync(original));
        Assert.Same(original, await manager.ReadAsync("item"));
        Assert.Same(updated, await manager.UpdateAsync(updated));
        await manager.DeleteAsync("item");

        Assert.Collection(recorder.Events,
            item => { Assert.Equal("Created", item.Operation); Assert.Same(original, item.Model); },
            item => { Assert.Equal("Read", item.Operation); Assert.Same(original, item.Model); },
            item => { Assert.Equal("Updated", item.Operation); Assert.Same(updated, item.Model); },
            item => { Assert.Equal("Deleted", item.Operation); Assert.Same(updated, item.Model); });

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.ReadAsync("item"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.UpdateAsync(updated));
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.DeleteAsync("item"));
        Assert.Equal(4, recorder.Events.Count);
    }

    [Fact]
    public async Task ShouldKeepSchoolCrudIndependentOfChildStacksAsync()
    {
        using ServiceProvider provider = CreateProvider();
        ISchoolManager schools = provider.GetRequiredService<ISchoolManager>();
        ITeacherManager teachers = provider.GetRequiredService<ITeacherManager>();
        var teacher = new Teacher { Id = "teacher", SchoolId = "school" };
        var school = new School { Id = "school", Teachers = new[] { teacher } };

        await schools.CreateAsync(school);
        await Assert.ThrowsAsync<InvalidOperationException>(() => teachers.ReadAsync("teacher"));
        await teachers.CreateAsync(teacher);
        await schools.DeleteAsync("school");
        Assert.Same(teacher, await teachers.ReadAsync("teacher"));
    }
}
