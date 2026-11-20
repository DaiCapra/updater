namespace Launcher.Core.Releases;

public interface IReleaseDownloader
{
    /// <summary>Downloads and verifies the release file, reporting progress from 0 to 1. Returns the local path.</summary>
    Task<string> DownloadAsync(UpdateRelease release, IProgress<double>? progress, CancellationToken cancellationToken);
}
