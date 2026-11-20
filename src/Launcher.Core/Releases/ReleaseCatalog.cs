using Launcher.Core.Storage;

namespace Launcher.Core.Releases;

public sealed record ReleaseEntry(Version Version, StoredFile File);

/// <summary>Maps the release files in a store listing to their versions.</summary>
public static class ReleaseCatalog
{
    /// <summary>Returns release files keyed by version (1.2 and 1.2.0 are the same key); other files are ignored.</summary>
    public static IReadOnlyDictionary<Version, ReleaseEntry> Build(IEnumerable<StoredFile> files, ReleaseFileNaming naming)
    {
        var releases = new Dictionary<Version, ReleaseEntry>(VersionComparer.Instance);
        foreach (var file in files)
        {
            if (naming.TryParse(file.Name, out var version) && !releases.TryAdd(version, new ReleaseEntry(version, file)))
            {
                throw new InvalidDataException(
                    $"The release location has more than one file for version {VersionComparer.Normalize(version)}. Remove the duplicate.");
            }
        }

        return releases;
    }
}
