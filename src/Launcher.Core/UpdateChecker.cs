using Launcher.Core.Releases;

namespace Launcher.Core;

public sealed class UpdateChecker(IUpdateSource source)
{
    public async Task<UpdateRelease?> CheckAsync(Version installedVersion, CancellationToken cancellationToken = default)
    {
        var release = await source.GetLatestReleaseAsync(cancellationToken).ConfigureAwait(false);
        return release is not null && VersionComparer.Instance.Compare(release.Version, installedVersion) > 0
            ? release
            : null;
    }
}
