// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.Collections.Generic;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Layout;
namespace StandardIo.ArchitectureDiagram.Core2.Exposures;
internal sealed class LayoutRuleFactory(IEnumerable<ILayoutRuleProcessingService> layoutRuleServices) : ILayoutRuleFactory
{
    public IEnumerable<ILayoutRuleProcessingService> GetLayoutRuleServices() => layoutRuleServices;
}
