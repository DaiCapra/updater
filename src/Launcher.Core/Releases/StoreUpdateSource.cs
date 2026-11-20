using Launcher.Core.Storage;

namespace Launcher.Core.Releases;

/// <summary>An update source for any release store: the highest-versioned release file wins.</summary>
public sealed class StoreUpdateSource(IReleaseStoreReader store, ReleaseFileNaming naming) : IUpdateSource
{
    public async Task<UpdateRelease?> GetLatestReleaseAsync(CancellationToken cancellationToken)
    {
        var files = await store.ListAsync(cancellationToken).ConfigureAwait(false);
        var releases = ReleaseCatalog.Build(files, naming);
        if (releases.Count == 0)
        {
            return null;
        }

        var latest = releases.Values.MaxBy(release => release.Version, VersionComparer.Instance)!;
        return new UpdateRelease(latest.Version, File: latest.File);
    }
}
