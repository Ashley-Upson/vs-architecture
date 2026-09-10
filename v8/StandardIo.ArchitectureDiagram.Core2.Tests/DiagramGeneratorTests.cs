// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------

using System;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core2.Models;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core2.Tests;

public sealed partial class DiagramGeneratorTests
{
    [Theory]
    [InlineData(DiagramTypes.Data)]
    public async Task ShouldRejectUnimplementedDataDiagramsAsync(DiagramTypes diagramType)
    {
        // Given
        var generator = new DiagramGenerator();
        var request = new DiagramGenerationRequest
        {
            ProjectPaths = new[] { "Example.csproj" },
            DiagramType = diagramType
        };

        // When
        Func<Task> generate = () => generator.GenerateAsync(request: request);

        // Then
        await Assert.ThrowsAsync<NotSupportedException>(testCode: generate);
    }

    [Fact]
    public async Task ShouldRejectNullRequestAsync()
    {
        // Given
        var generator = new DiagramGenerator();

        // When
        Func<Task> generate = () => generator.GenerateAsync(request: null!);

        // Then
        await Assert.ThrowsAsync<ArgumentNullException>(testCode: generate);
    }
}