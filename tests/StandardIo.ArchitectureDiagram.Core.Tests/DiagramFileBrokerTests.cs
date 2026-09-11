using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StandardIo.ArchitectureDiagram.Core.Brokers.Files;
using Xunit;

namespace StandardIo.ArchitectureDiagram.Core.Tests;

public sealed class DiagramFileBrokerTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"standard-io-file-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task WriteTextAsync_commits_new_destination_without_backup_or_temporary_file()
    {
        var destination = Destination();

        await new DiagramFileBroker().WriteTextAsync(destination, "new content");

        Assert.Equal("new content", File.ReadAllText(destination));
        AssertNoAuxiliaryFiles();
    }

    [Fact]
    public async Task WriteTextAsync_replaces_existing_destination_after_content_is_complete()
    {
        var destination = Destination();
        Directory.CreateDirectory(directory);
        File.WriteAllText(destination, "old content");

        await new DiagramFileBroker().WriteTextAsync(destination, "replacement");

        Assert.Equal("replacement", File.ReadAllText(destination));
        AssertNoAuxiliaryFiles();
    }

    [Fact]
    public async Task WriteTextAsync_preserves_existing_destination_and_cleans_temporary_file_on_write_failure()
    {
        var destination = ExistingDestination();
        var fileSystem = new ControlledFileSystem { WriteException = new IOException("simulated write failure") };

        await Assert.ThrowsAsync<IOException>(() =>
            new DiagramFileBroker(fileSystem).WriteTextAsync(destination, "replacement"));

        Assert.Equal("old content", File.ReadAllText(destination));
        Assert.False(File.Exists(fileSystem.TemporaryPath));
        Assert.False(fileSystem.CommitCalled);
    }

    [Fact]
    public async Task WriteTextAsync_preserves_existing_destination_when_cancelled_before_commit()
    {
        var destination = ExistingDestination();
        using var cancellation = new CancellationTokenSource();
        var fileSystem = new ControlledFileSystem { AfterWrite = cancellation.Cancel };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new DiagramFileBroker(fileSystem).WriteTextAsync(destination, "replacement", cancellation.Token));

        Assert.Equal("old content", File.ReadAllText(destination));
        Assert.False(File.Exists(fileSystem.TemporaryPath));
        Assert.False(fileSystem.CommitCalled);
    }

    [Fact]
    public async Task WriteTextAsync_preserves_large_unicode_content()
    {
        var destination = Destination();
        var content = new StringBuilder(200_000);
        for (var index = 0; index < 20_000; index++)
        {
            content.Append("route → 測試 🛣️\n");
        }

        await new DiagramFileBroker().WriteTextAsync(destination, content.ToString());

        Assert.Equal(content.ToString(), File.ReadAllText(destination));
        AssertNoAuxiliaryFiles();
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    private string Destination() => Path.Combine(directory, "diagram.drawio");

    private string ExistingDestination()
    {
        Directory.CreateDirectory(directory);
        var destination = Destination();
        File.WriteAllText(destination, "old content");
        return destination;
    }

    private void AssertNoAuxiliaryFiles() =>
        Assert.Equal(new[] { "diagram.drawio" }, Directory.GetFiles(directory).Select(Path.GetFileName));

    private sealed class ControlledFileSystem : IDiagramFileSystem
    {
        private readonly DiagramFileSystem inner = new();

        public Exception? WriteException { get; init; }
        public Action? AfterWrite { get; init; }
        public string TemporaryPath { get; private set; } = string.Empty;
        public bool CommitCalled { get; private set; }

        public void CreateDirectory(string path) => inner.CreateDirectory(path);

        public string CreateTemporaryPath(string destinationPath) =>
            TemporaryPath = inner.CreateTemporaryPath(destinationPath);

        public async Task WriteTextAsync(string path, string content, CancellationToken cancellationToken)
        {
            await inner.WriteTextAsync(path, content, CancellationToken.None);
            if (WriteException is not null)
            {
                throw WriteException;
            }

            AfterWrite?.Invoke();
        }

        public void Commit(string temporaryPath, string destinationPath)
        {
            CommitCalled = true;
            inner.Commit(temporaryPath, destinationPath);
        }

        public void DeleteIfExists(string path) => inner.DeleteIfExists(path);
    }
}
