// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using Microsoft.Extensions.DependencyInjection;
namespace StandardIo.ArchitectureDiagram.Core2.Tests;
internal static class TestServices
{
    private static readonly ServiceProvider provider = new ServiceCollection().AddArchitectureDiagram().BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    internal static T Get<T>() where T : notnull => provider.GetRequiredService<T>();
}
