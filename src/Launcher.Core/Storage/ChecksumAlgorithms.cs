using System.Security.Cryptography;

namespace Launcher.Core.Storage;

/// <summary>The checksum algorithms stores may report. Add new algorithms here.</summary>
public static class ChecksumAlgorithms
{
    private static readonly Dictionary<string, HashAlgorithmName> Algorithms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["md5"] = HashAlgorithmName.MD5,
        ["sha256"] = HashAlgorithmName.SHA256
    };

    public static void EnsureSupported(string algorithm)
    {
        if (!Algorithms.ContainsKey(algorithm))
        {
            throw new NotSupportedException($"The checksum algorithm '{algorithm}' is not supported.");
        }
    }

    public static async Task<FileChecksum> ComputeAsync(Stream content, string algorithm, CancellationToken cancellationToken)
    {
        EnsureSupported(algorithm);
        using var hash = IncrementalHash.CreateHash(Algorithms[algorithm]);
        var buffer = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            hash.AppendData(buffer, 0, read);
        }

        return new FileChecksum(algorithm, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }

    public static async Task<bool> MatchesAsync(Stream content, FileChecksum expected, CancellationToken cancellationToken)
    {
        var actual = await ComputeAsync(content, expected.Algorithm, cancellationToken).ConfigureAwait(false);
        return string.Equals(actual.Value, expected.Value, StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<bool> MatchesAsync(string path, FileChecksum expected, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return await MatchesAsync(stream, expected, cancellationToken).ConfigureAwait(false);
    }
}
