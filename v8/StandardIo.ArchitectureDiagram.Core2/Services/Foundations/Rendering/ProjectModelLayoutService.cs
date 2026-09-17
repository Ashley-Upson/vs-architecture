// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using StandardIo.ArchitectureDiagram.Core2.Brokers.Rendering;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Rendering;
internal sealed class ProjectModelLayoutService(IProjectModelLayoutBroker projectModelLayoutBroker, ILayoutInitializationService initializationService) : IProjectModelLayoutService
{
    public RenderModel Layout(RenderModel renderModel)
    {
        ArgumentNullException.ThrowIfNull(renderModel);
        int maxIterations = renderModel.Configuration.MaxLayoutIterations;
        if (maxIterations <= 0) throw new ArgumentOutOfRangeException(nameof(maxIterations));
        if (!renderModel.LayoutInitialized && renderModel.Projects.Length > 0) initializationService.Initialize(renderModel);
        var rules = projectModelLayoutBroker.GetLayoutRuleServices().ToArray();
        var seen = new HashSet<string>();
        string[] errors = [];
        string[] changed = [];
        for (int iteration = 0; iteration < maxIterations; iteration++)
        {
            renderModel.LayoutIterations = iteration + 1;
            string before = LayoutState.Capture(renderModel);
            var adjustments = new List<string>();
            string previousRule = before;
            foreach (var rule in rules)
            {
                rule.ApplyRule(renderModel);
                string afterRule = LayoutState.Capture(renderModel);
                if (afterRule != previousRule) adjustments.Add(rule.GetType().Name);
                previousRule = afterRule;
            }
            errors = rules.SelectMany(rule => rule.GetViolations(renderModel).Select(message => rule.GetType().Name + ": " + message)).ToArray();
            changed = adjustments.ToArray();
            if (changed.Length == 0 && errors.Length == 0) return renderModel;
            if (changed.Length == 0)
                throw new InvalidOperationException($"Stable layout has unmet conditions: {string.Join("; ", errors)}");
            if (changed.Length > 0 && !seen.Add(previousRule))
                throw new InvalidOperationException($"Layout rules repeat a non-final state. Changing rules: {string.Join(", ", changed)}. Conditions: {string.Join("; ", errors)}");
        }
        throw new InvalidOperationException($"Layout did not converge after {maxIterations} iterations. Changing rules: {string.Join(", ", changed)}. Conditions: {string.Join("; ", errors)}");
    }

}
