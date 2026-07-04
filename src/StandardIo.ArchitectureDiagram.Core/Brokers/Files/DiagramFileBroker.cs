using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace StandardIo.ArchitectureDiagram.Core.Brokers.Files;

public sealed class DiagramFileBroker : IDiagramFileBroker
{
    public async Task WriteTextAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Output path is required.", nameof(path));
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var writer = new StreamWriter(path, false);
        await writer.WriteAsync(content ?? string.Empty).ConfigureAwait(false);
    }
}
