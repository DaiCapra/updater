using Launcher.Core.Storage;

namespace Launcher.Core.Releases;

/// <summary>
/// Downloads a release file from any store into a staging directory. The file is written as
/// <c>.partial</c>, checked against the store's size and checksum, and only then renamed.
/// </summary>
public sealed class StoreReleaseDownloader(IReleaseStoreReader store, string stagingDirectory) : IReleaseDownloader
{
    public async Task<string> DownloadAsync(UpdateRelease release, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var file = release.File ?? throw new ArgumentException("The release has no stored file to download.", nameof(release));
        if (file.Name.Length == 0 || Path.GetFileName(file.Name) != file.Name)
        {
            throw new InvalidDataException($"The release file name '{file.Name}' is not a plain file name.");
        }

        if (file.Checksum is { } checksum)
        {
            ChecksumAlgorithms.EnsureSupported(checksum.Algorithm);
        }

        Directory.CreateDirectory(stagingDirectory);
        var target = Path.Combine(stagingDirectory, file.Name);
        if (File.Exists(target))
        {
            if (await IsVerifiedAsync(target, file, cancellationToken).ConfigureAwait(false))
            {
                progress?.Report(1);
                return target;
            }

            File.Delete(target);
        }

        var partial = target + ".partial";
        try
        {
            await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var bytes = new InlineProgress<long>(sent => progress?.Report(file.Size > 0 ? Math.Min(1, (double)sent / file.Size) : 0));
                await store.DownloadAsync(file, output, bytes, cancellationToken).ConfigureAwait(false);
            }

            if (!await IsVerifiedAsync(partial, file, cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidDataException($"The downloaded file {file.Name} doesn't match the size or checksum reported by the store.");
            }

            File.Move(partial, target, overwrite: true);
            progress?.Report(1);
            return target;
        }
        catch
        {
            File.Delete(partial);
            throw;
        }
    }

    private static async Task<bool> IsVerifiedAsync(string path, StoredFile file, CancellationToken cancellationToken) =>
        new FileInfo(path).Length == file.Size
        && (file.Checksum is not { } checksum
            || await ChecksumAlgorithms.MatchesAsync(path, checksum, cancellationToken).ConfigureAwait(false));

    /// <summary>Reports synchronously, unlike <see cref="Progress{T}"/>, so the caller decides which thread to marshal to.</summary>
    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
