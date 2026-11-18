namespace Launcher.Core;

public interface IUpdateSource
{
    Task<UpdateRelease?> GetLatestReleaseAsync(CancellationToken cancellationToken);
}

public sealed record UpdateRelease(Version Version, Uri DownloadUrl);
