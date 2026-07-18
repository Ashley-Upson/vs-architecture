using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace StandardIo.ArchitectureDiagram.Core.Brokers.Files;

public sealed class DiagramFileBroker : IDiagramFileBroker
{
    private readonly IDiagramFileSystem fileSystem;

    public DiagramFileBroker()
        : this(new DiagramFileSystem())
    { }

    internal DiagramFileBroker(IDiagramFileSystem fileSystem) =>
        this.fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

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
            fileSystem.CreateDirectory(directory);
        }

        var temporaryPath = fileSystem.CreateTemporaryPath(path);
        try
        {
            await fileSystem.WriteTextAsync(temporaryPath, content ?? string.Empty, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            fileSystem.Commit(temporaryPath, path);
        }
        finally
        {
            fileSystem.DeleteIfExists(temporaryPath);
        }
    }
}

internal interface IDiagramFileSystem
{
    void CreateDirectory(string path);
    string CreateTemporaryPath(string destinationPath);
    Task WriteTextAsync(string path, string content, CancellationToken cancellationToken);
    void Commit(string temporaryPath, string destinationPath);
    void DeleteIfExists(string path);
}

internal sealed class DiagramFileSystem : IDiagramFileSystem
{
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public string CreateTemporaryPath(string destinationPath)
    {
        var directory = Path.GetDirectoryName(destinationPath);
        var name = Path.GetFileName(destinationPath);
        return Path.Combine(directory ?? string.Empty, $".{name}.{Guid.NewGuid():N}.tmp");
    }

    public async Task WriteTextAsync(string path, string content, CancellationToken cancellationToken)
    {
        const int BufferSize = 4096;
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, true);
        using var writer = new StreamWriter(stream);
        for (var offset = 0; offset < content.Length; offset += BufferSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(BufferSize, content.Length - offset);
            await writer.WriteAsync(content.Substring(offset, count)).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        await writer.FlushAsync().ConfigureAwait(false);
        stream.Flush(true);
    }

    public void Commit(string temporaryPath, string destinationPath)
    {
        if (File.Exists(destinationPath))
        {
            File.Replace(temporaryPath, destinationPath, null);
        }
        else
        {
            File.Move(temporaryPath, destinationPath);
        }
    }

    public void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
