// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core2.Models;
using Xunit;
using StandardIo.ArchitectureDiagram.Core2.Exposures;

namespace StandardIo.ArchitectureDiagram.Core2.Tests;
public sealed partial class DiagramGeneratorTests
{
    [Fact]
    public async Task ShouldRejectNullRequestAsync()
    {
        // Given
        var generator = TestServices.Get<DiagramGenerator>();
        // When
        Func<Task> generate = () => generator.GenerateAsync(request: null !);
        // Then
        await Assert.ThrowsAsync<ArgumentNullException>(testCode: generate);
    }
}