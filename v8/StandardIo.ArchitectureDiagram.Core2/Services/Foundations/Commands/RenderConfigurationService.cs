// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Brokers.Files;
namespace StandardIo.ArchitectureDiagram.Core2.Services.Foundations.Commands;
internal sealed class RenderConfigurationService(IRenderConfigurationBroker broker) : IRenderConfigurationService
{
    public RenderConfiguration Load(string? path)
    {
        var configuration = path is null ? new RenderConfiguration() :
            JsonSerializer.Deserialize<RenderConfiguration>(broker.ReadConfiguration(path), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
            }) ?? throw new ArgumentException("Render configuration cannot be null.");
        return configuration;
    }
    public void Validate(RenderConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(configuration.Architecture);
        ArgumentNullException.ThrowIfNull(configuration.CallChain);
        ArgumentNullException.ThrowIfNull(configuration.DataModel);
        Positive(configuration.HorizontalOffset, nameof(configuration.HorizontalOffset));
        if (configuration.MaxLayoutIterations <= 0) throw new ArgumentOutOfRangeException(nameof(configuration.MaxLayoutIterations));
        var architecture = configuration.Architecture;
        Positive(architecture.RowDepth - 60, nameof(architecture.RowDepth));
        Positive(architecture.NodeWidth - 10, nameof(architecture.NodeWidth));
        Positive(architecture.NodeSpacing, nameof(architecture.NodeSpacing));
        Positive(architecture.ProjectSpacing, nameof(architecture.ProjectSpacing));
        if (architecture.LayerColours is not { Length: 7 } || architecture.LayerColours.Any(colour => colour is null || !Regex.IsMatch(colour, "^#[0-9a-fA-F]{6}$")))
            throw new ArgumentException("LayerColours must contain seven #RRGGBB colours.");
    }
    private static void Positive(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(name);
    }
}
