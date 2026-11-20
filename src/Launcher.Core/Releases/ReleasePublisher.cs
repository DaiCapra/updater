using Launcher.Core.Storage;

namespace Launcher.Core.Releases;

public sealed record PublishResult(Version Version, StoredFile? Uploaded);

/// <summary>Publishes a release file to any store: validates the name, refuses existing versions, and verifies the upload.</summary>
public sealed class ReleasePublisher(IReleaseStoreReader reader, IReleaseStoreWriter writer, ReleaseFileNaming naming)
{
    public async Task<PublishResult> PublishAsync(
        string filePath, bool dryRun, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        var name = Path.GetFileName(filePath);
        if (!naming.TryParse(name, out var version))
        {
            throw new InvalidDataException($"The release file must be named like {naming.Format(new Version(1, 2, 3))}, not {name}.");
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"The release file {filePath} doesn't exist.", filePath);
        }

        var existing = ReleaseCatalog.Build(await reader.ListAsync(cancellationToken).ConfigureAwait(false), naming);
        if (existing.TryGetValue(version, out var published))
        {
            throw new InvalidOperationException($"Version {version} is already published as {published.File.Name}.");
        }

        if (dryRun)
        {
            return new PublishResult(version, null);
        }

        await using var content = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        var uploaded = await writer.UploadAsync(name, content, progress, cancellationToken).ConfigureAwait(false);

        content.Position = 0;
        if (uploaded.Size != content.Length
            || (uploaded.Checksum is { } checksum
                && !await ChecksumAlgorithms.MatchesAsync(content, checksum, cancellationToken).ConfigureAwait(false)))
        {
            throw new InvalidDataException($"The uploaded file {uploaded.Name} doesn't match the local file. Delete it and publish again.");
        }

        return new PublishResult(version, uploaded);
    }
}
