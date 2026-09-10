// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Collections.Generic;
using StandardIo.ArchitectureDiagram.Core2.Exposures;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
namespace StandardIo.ArchitectureDiagram.Core2.Brokers.Rendering;
internal sealed class ProjectModelLayoutBroker(ILayoutRuleFactory layoutRuleFactory) : IProjectModelLayoutBroker
{
    public IEnumerable<ILayoutRuleProcessingService> GetLayoutRuleServices() => layoutRuleFactory.GetLayoutRuleServices();
}
