// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Processings.Rendering;
internal sealed class DeferredExecutionRenderModelProcessingService : IDeferredExecutionRenderModelProcessingService
{
    public void Apply(RenderModel renderModel)
    {
        if (!renderModel.Configuration.Architecture.DeferredExecutionSupport || renderModel.DiagramType != DiagramTypes.Architecture) return;
        var raw = renderModel.ProjectModels.SelectMany(project => project.Dependencies ?? []).ToArray();
        var knownTypes = renderModel.ProjectModels.SelectMany(project => project.Types ?? []).ToArray();
        var callbacks = raw.Where(link => link.ExecutorType is not null && knownTypes.Any(type => type.Name == link.ExecutorType)).ToArray();
        if (callbacks.Length == 0) return;
        var nodes = renderModel.Projects.ToDictionary(project => project.Id, project => project.Nodes.ToList());
        var owners = renderModel.Projects.SelectMany(project => project.Nodes.Select(node => (node.Id, project.Id)))
            .ToDictionary(pair => pair.Item1, pair => pair.Item2);
        var originalNodes = renderModel.Projects.SelectMany(project => project.Nodes).ToArray();
        var edges = renderModel.Projects.SelectMany(project => project.Connections).Concat(renderModel.CrossProjectConnections)
            .Where(edge => !callbacks.Any(call => call.FromType == edge.FromType && call.ToType == edge.ToType)
                || raw.Any(call => call.FromType == edge.FromType && call.ToType == edge.ToType
                    && (call.ExecutorType is null || call.IsInjected))).ToList();
        int occurrence = 0;
        RenderNode Copy(RenderNode template, string owner, string role, string bus)
        {
            string label = template.Label.Split('\n')[0] + "\n" + role + " · " + bus;
            var copy = template with
            {
                Id = owner + "-communication-" + occurrence++, Label = label,
                CommunicationRole = role, CommunicationId = template.TypeName,
                TextLines = [new RenderText(label.Split('\n')[0], template.X + template.Width / 2, template.Y + 22, true),
                    new RenderText(role + " · " + bus, template.X + template.Width / 2, template.Y + 38)]
            };
            nodes[owner].Add(copy);
            owners.Add(copy.Id, owner);
            return copy;
        }
        int busIndex = 0;
        foreach (var executor in callbacks.GroupBy(call => call.ExecutorType!).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var originals = originalNodes.Where(node => node.TypeName == executor.Key).ToArray();
            var definition = knownTypes.First(type => type.Name == executor.Key);
            var templateNode = originals.FirstOrDefault() ?? new RenderNode("boundary", executor.Key,
                System.Text.RegularExpressions.Regex.Replace(executor.Key, @"(?:[A-Za-z_]\w*\.)+", ""),
                Foundations.Rendering.DiagramStyles.GetRoleColour(executor.Key, definition.IsInternal, renderModel.Configuration.Architecture.LayerColours),
                40, 60, renderModel.Configuration.Architecture.NodeWidth, 60, []);
            string bus = "bus " + ++busIndex;
            var registrations = new HashSet<string>();
            foreach (var incoming in edges.Where(edge => edge.ToType == executor.Key).ToArray())
            {
                var template = originals.FirstOrDefault(node => node.Id == incoming.TargetId) ?? templateNode;
                var copy = Copy(template, owners[incoming.SourceId], "registration", bus);
                registrations.Add(incoming.SourceId);
                int index = edges.IndexOf(incoming);
                edges[index] = incoming with { TargetId = copy.Id };
            }
            foreach (var registrar in originalNodes.Where(node => executor.Any(call => call.RegistrationType == node.TypeName)))
            {
                if (!registrations.Add(registrar.Id)) continue;
                var copy = Copy(templateNode, owners[registrar.Id], "registration", bus);
                edges.Add(new RenderConnection(copy.Id + "-register", registrar.Id, copy.Id, registrar.TypeName, executor.Key,
                    false, [], renderModel.Configuration.ColourLines ? copy.Fill : "#d1d5db"));
            }
            foreach (var target in originalNodes.Where(node => executor.Any(call => call.ToType == node.TypeName)))
            {
                var copy = Copy(templateNode, owners[target.Id], "execution", bus);
                edges.Add(new RenderConnection(copy.Id + "-dispatch", copy.Id, target.Id, executor.Key, target.TypeName,
                    false, [], renderModel.Configuration.ColourLines ? target.Fill : "#d1d5db") { IsDeferred = true });
            }
            foreach (var original in originals)
                if (!edges.Any(edge => edge.SourceId == original.Id || edge.TargetId == original.Id))
                    nodes[owners[original.Id]].Remove(original);
        }
        renderModel.Projects = renderModel.Projects.Select(project => project with
        {
            Nodes = nodes[project.Id].ToArray(),
            Connections = edges.Where(edge => owners[edge.SourceId] == project.Id && owners[edge.TargetId] == project.Id).ToArray()
        }).Where(project => project.Nodes.Length > 0).ToArray();
        renderModel.CrossProjectConnections = edges.Where(edge => owners[edge.SourceId] != owners[edge.TargetId]).ToArray();
    }
}
