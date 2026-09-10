// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using StandardIo.ArchitectureDiagram.Core2.Models;
using StandardIo.ArchitectureDiagram.Core2.Services.Processings.Commands;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public sealed partial class EnumCommandTests
{
    [Theory]
    [InlineData("unknown")]
    [InlineData("0")]
    [InlineData("999")]
    [InlineData("Html, DrawIO")]
    public void ShouldRejectInvalidFormatsDuringParsing(string format)
    {
        // Given
        var parser = TestServices.Get<StandardIo.ArchitectureDiagram.Core2.Services.Processings.Commands.ICommandParserProcessingService>();
        // When
        Action parse = () => parser.Parse(command: new[] { "Architecture", "project.csproj", "-o", "output", "-f", format });
        // Then
        Assert.Throws<ArgumentException>(testCode: parse);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("0")]
    [InlineData("999")]
    [InlineData("Architecture, Data")]
    public void ShouldRejectInvalidDiagramTypesDuringParsing(string diagramType)
    {
        // Given
        var parser = TestServices.Get<StandardIo.ArchitectureDiagram.Core2.Services.Processings.Commands.ICommandParserProcessingService>();
        // When
        Action parse = () => parser.Parse(command: new[] { diagramType, "project.csproj", "-o", "output" });
        // Then
        Assert.Throws<ArgumentException>(testCode: parse);
    }

    [Theory]
    [InlineData(" architecture ", " HTML ", DiagramTypes.Architecture, DiagramFormats.Html)]
    [InlineData("data", "drawio", DiagramTypes.DataModel, DiagramFormats.DrawIO)]
    public void ShouldParseNamesIntoEnums(string diagramType, string format, DiagramTypes expectedType, DiagramFormats expectedFormat)
    {
        // Given
        var parser = TestServices.Get<StandardIo.ArchitectureDiagram.Core2.Services.Processings.Commands.ICommandParserProcessingService>();
        // When
        var request = parser.Parse(command: new[] { diagramType, "project.csproj", "-o", "output.anything", "-f", format });
        // Then
        Assert.Equal(expected: expectedType, actual: request.DiagramType);
        Assert.Equal(expected: expectedFormat, actual: request.Format);
    }
}