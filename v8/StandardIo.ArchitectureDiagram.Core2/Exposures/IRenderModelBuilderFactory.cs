// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
namespace StandardIo.ArchitectureDiagram.Core2.Exposures;
internal interface IRenderModelBuilderFactory
{
    IRenderModelBuilder CreateRenderModelBuilder(string namedKey);
}
