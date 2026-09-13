// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using Microsoft.Extensions.DependencyInjection;
namespace StandardIo.ArchitectureDiagram.Core2.Exposures;
internal sealed class RenderModelBuilderFactory(IServiceProvider serviceProvider) : IRenderModelBuilderFactory
{
    public IRenderModelBuilder CreateRenderModelBuilder(string namedKey) =>
        serviceProvider.GetRequiredKeyedService<IRenderModelBuilder>(serviceKey: namedKey);
}
