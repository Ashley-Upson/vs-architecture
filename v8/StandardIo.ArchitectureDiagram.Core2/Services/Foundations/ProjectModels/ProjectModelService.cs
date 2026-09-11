// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;

namespace StandardIo.ArchitectureDiagram.Core2.Services.Foundations.ProjectModels;
internal sealed class ProjectModelService : IProjectModelService
{
    public ProjectModel CreateProjectModel(ProjectModel source, DefinedType[] types, TypeRelationship[] dependencies) =>
        new ProjectModel
    {
        Name = source.Name,
        Path = source.Path,
        Types = types.Select(selector: type => new DefinedType { Name = type.Name, IsDataType = type.IsDataType, BaseTypeName = type.BaseTypeName, InterfaceNames = type.InterfaceNames?.ToArray(), AssemblyName = type.AssemblyName, FrameworkType = type.FrameworkType, IsInternal = type.IsInternal, Fields = (type.Fields ?? Array.Empty<Field>()).Select(selector: field => new Field { Name = field.Name, Type = field.Type })
            .ToArray(), Properties = (type.Properties ?? Array.Empty<Property>()).Select(selector: property => new Property { Name = property.Name, Type = property.Type })
            .ToArray(), Methods = (type.Methods ?? Array.Empty<Method>()).Select(selector: method => new Method { Name = method.Name })
            .ToArray() })
            .ToArray(),
        Dependencies = dependencies.Select(selector: dependency => new TypeRelationship { DependencyType = dependency.DependencyType, FromType = dependency.FromType, ToType = dependency.ToType, FromMethod = dependency.FromMethod, ToMethod = dependency.ToMethod, ExecutorType = dependency.ExecutorType, RegistrationType = dependency.RegistrationType, IsInjected = dependency.IsInjected })
            .ToArray()
    };
}