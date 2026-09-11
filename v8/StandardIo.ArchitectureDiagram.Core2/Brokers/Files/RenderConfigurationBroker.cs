// ---------------------------------------------------------------
// Copyright (c) Paul.Ward@ccoder.co.uk
// ---------------------------------------------------------------
using System.IO;
namespace StandardIo.ArchitectureDiagram.Core2.Brokers.Files;
internal sealed class RenderConfigurationBroker : IRenderConfigurationBroker
{
    public string ReadConfiguration(string path) => File.ReadAllText(path);
}
