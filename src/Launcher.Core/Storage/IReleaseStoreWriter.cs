namespace Launcher.Core.Storage;

/// <summary>Write access to a release location. Only publishing tools receive this; the launcher never does.</summary>
public interface IReleaseStoreWriter
{
    /// <summary>Uploads <paramref name="content"/> as <paramref name="name"/>, reporting bytes sent.</summary>
    Task<StoredFile> UploadAsync(string name, Stream content, IProgress<long>? progress, CancellationToken cancellationToken);

    Task DeleteAsync(StoredFile file, CancellationToken cancellationToken);
}
