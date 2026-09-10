// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using StandardIo.ArchitectureDiagram.Core2.Models;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Commands;
internal interface IRenderConfigurationService
{
    RenderConfiguration Load(string? path);
    void Validate(RenderConfiguration configuration);
}
