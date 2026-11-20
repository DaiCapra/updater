using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Launcher.Core;
using Launcher.Core.Storage;
using Launcher.Services.Providers;

namespace Launcher.Tests.Fakes;

/// <summary>A release store held in memory. It drives the host-agnostic tests and runs the store contract suite.</summary>
internal sealed class InMemoryReleaseStore : IReleaseStoreReader, IReleaseStoreWriter
{
    private readonly Dictionary<string, (StoredFile File, byte[] Content)> files = [];

    public int Downloads { get; private set; }
    public int Uploads { get; private set; }

    /// <summary>Adds a file directly; <paramref name="reportedChecksum"/> overrides the checksum the store reports.</summary>
    public StoredFile Add(string name, byte[] content, FileChecksum? reportedChecksum = null, long? reportedSize = null)
    {
        var file = new StoredFile(
            Guid.NewGuid().ToString("N"),
            name,
            reportedSize ?? content.Length,
            reportedChecksum ?? new FileChecksum("md5", Convert.ToHexStringLower(MD5.HashData(content))));
        files[file.Key] = (file, content);
        return file;
    }

    public Task<IReadOnlyList<StoredFile>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<StoredFile>>(files.Values.Select(entry => entry.File).ToList());
    }

    public async Task DownloadAsync(StoredFile file, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!files.TryGetValue(file.Key, out var entry))
        {
            throw new ReleaseStoreException(ReleaseStoreErrorKind.NotFound, $"{file.Name} doesn't exist.");
        }

        Downloads++;
        const int chunk = 4;
        for (var offset = 0; offset < entry.Content.Length; offset += chunk)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(chunk, entry.Content.Length - offset);
            await destination.WriteAsync(entry.Content.AsMemory(offset, count), cancellationToken);
            progress?.Report(offset + count);
        }
    }

    public async Task<StoredFile> UploadAsync(string name, Stream content, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        Uploads++;
        progress?.Report(buffer.Length);
        return Add(name, buffer.ToArray());
    }

    public Task DeleteAsync(StoredFile file, CancellationToken cancellationToken)
    {
        files.Remove(file.Key);
        return Task.CompletedTask;
    }
}

internal sealed record InMemorySettings(string Name) : ISourceSettings;

/// <summary>A provider registered under the <c>memory</c> type, proving the composition roots accept any host.</summary>
internal sealed class InMemoryStorageProvider(InMemoryReleaseStore store) : IStorageProvider
{
    public string Type => "memory";

    public ISourceSettings Parse(JsonElement json, string baseDirectory) =>
        new InMemorySettings(SettingsJson.RequireString(json, "name", Type));

    public IReleaseStoreReader CreateReader(ISourceSettings settings) => store;

    public IReleaseStoreWriter CreateWriter(ISourceSettings settings) => store;
}
