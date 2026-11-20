using Launcher.Core.Storage;

namespace Launcher.Core;

public interface IUpdateSource
{
    Task<UpdateRelease?> GetLatestReleaseAsync(CancellationToken cancellationToken);
}

/// <summary>
/// A release found by an update source. Manifest sources point at a download page; storage-backed
/// sources carry the stored file so the launcher can download it itself.
/// </summary>
public sealed record UpdateRelease(Version Version, Uri? DownloadPage = null, StoredFile? File = null);
