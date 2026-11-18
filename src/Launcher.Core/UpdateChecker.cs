namespace Launcher.Core;

public sealed class UpdateChecker(IUpdateSource source)
{
    public async Task<UpdateRelease?> CheckAsync(Version installedVersion, CancellationToken cancellationToken = default)
    {
        var release = await source.GetLatestReleaseAsync(cancellationToken).ConfigureAwait(false);
        return release is not null && Normalize(release.Version) > Normalize(installedVersion)
            ? release
            : null;
    }

    private static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
}
