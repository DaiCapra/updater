namespace Launcher.Core.Storage;

/// <summary>Read access to the files of one release location (a Drive folder, a bucket prefix, ...).</summary>
public interface IReleaseStoreReader
{
    Task<IReadOnlyList<StoredFile>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Copies the file's content to <paramref name="destination"/>, reporting bytes transferred.</summary>
    Task DownloadAsync(StoredFile file, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken);
}
