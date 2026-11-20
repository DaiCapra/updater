using System.IO.Compression;

namespace Launcher.Core.Installation;

public interface IReleaseInstaller
{
    /// <summary>Installs the release archive and makes it the active build, reporting progress from 0 to 1.</summary>
    Task<InstalledBuild> InstallAsync(string archivePath, Version version, IProgress<double>? progress, CancellationToken cancellationToken);
}

/// <summary>
/// Extracts a ZIP build next to the current one and switches to it only once extraction succeeded,
/// so a failed or cancelled update leaves the installed build playable.
/// </summary>
public sealed class ZipReleaseInstaller(InstallationStore store) : IReleaseInstaller
{
    public async Task<InstalledBuild> InstallAsync(
        string archivePath, Version version, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var folder = version.ToString();
        var target = Path.Combine(store.VersionsDirectory, folder);
        var partial = target + ".partial";
        TryDelete(() => Directory.Delete(partial, recursive: true));

        try
        {
            await Task.Run(() => Extract(archivePath, partial, progress, cancellationToken), cancellationToken).ConfigureAwait(false);

            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }

            Directory.Move(BuildRoot(partial), target);
            store.Activate(version, folder);
        }
        finally
        {
            TryDelete(() => Directory.Delete(partial, recursive: true));
        }

        RemoveOtherVersions(folder);
        TryDelete(() => File.Delete(archivePath));
        progress?.Report(1);
        return store.Current ?? throw new IOException($"The build for version {version} was installed but can't be found.");
    }

    private static void Extract(string archivePath, string destination, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var total = Math.Max(1, archive.Entries.Sum(entry => entry.Length));
        long written = 0;
        var buffer = new byte[81920];

        Directory.CreateDirectory(destination);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!PathSafety.TryResolveInside(destination, entry.FullName, out var path))
            {
                throw new InvalidDataException($"The build archive contains an entry outside the install folder: {entry.FullName}");
            }

            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                Directory.CreateDirectory(path);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var input = entry.Open();
            using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            int read;
            while ((read = input.Read(buffer)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                output.Write(buffer, 0, read);
                written += read;
                progress?.Report((double)written / total);
            }
        }
    }

    /// <summary>Archives often wrap the build in one folder; use that folder as the build root.</summary>
    private static string BuildRoot(string extracted)
    {
        var directories = Directory.GetDirectories(extracted);
        return directories.Length == 1 && Directory.GetFiles(extracted).Length == 0 ? directories[0] : extracted;
    }

    private void RemoveOtherVersions(string keep)
    {
        if (!Directory.Exists(store.VersionsDirectory))
        {
            return;
        }

        foreach (var directory in Directory.GetDirectories(store.VersionsDirectory))
        {
            if (!string.Equals(Path.GetFileName(directory), keep, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(() => Directory.Delete(directory, recursive: true));
            }
        }
    }

    // Old builds or the archive may be locked (for example while the app runs); the next install retries.
    private static void TryDelete(Action delete)
    {
        try
        {
            delete();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }
}
