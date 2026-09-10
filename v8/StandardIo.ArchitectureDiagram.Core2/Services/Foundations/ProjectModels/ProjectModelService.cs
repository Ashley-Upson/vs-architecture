// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;

namespace StandardIo.ArchitectureDiagram.Core2.Services.Foundations.ProjectModels;

internal sealed class ProjectModelService : IProjectModelService
{
    public ProjectModel CreateProjectModel(ProjectModel source, DefinedType[] types, Dependency[] dependencies) => new ProjectModel
    {
        Name = source.Name,
        Path = source.Path,
        Types = types.Select(type => new DefinedType
        {
            Name = type.Name,
            FrameworkType = type.FrameworkType,
            IsInternal = type.IsInternal,
            Fields = (type.Fields ?? Array.Empty<Field>()).Select(field => new Field { Name = field.Name, Type = field.Type }).ToArray(),
            Properties = (type.Properties ?? Array.Empty<Property>()).Select(property => new Property { Name = property.Name, Type = property.Type }).ToArray(),
            Methods = (type.Methods ?? Array.Empty<Method>()).Select(method => new Method { Name = method.Name }).ToArray()
        }).ToArray(),
        Dependencies = dependencies.Select(dependency => new Dependency
        {
            DependencyType = dependency.DependencyType,
            FromType = dependency.FromType,
            ToType = dependency.ToType,
            FromMethod = dependency.FromMethod,
            ToMethod = dependency.ToMethod
        }).ToArray()
    };
}
