using System.Text.Json;
using Launcher.Core;
using Launcher.Core.Releases;
using Launcher.Core.Storage;
using Launcher.Services;
using Launcher.Services.Providers;

namespace Launcher.Publish;

/// <summary>
/// <c>Launcher.Publish --target publish.json --file build-1.3.0.zip [--dry-run]</c>. The target file uses
/// the same <c>{ "type": ..., ... }</c> shape as a profile's <c>updateSource</c>, with an upload-capable key.
/// </summary>
internal sealed class PublishCommand(ProviderRegistry providers, TextWriter output, TextWriter errors)
{
    internal const string Usage = "Usage: Launcher.Publish --target publish.json --file build-<version>.zip [--dry-run]";

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (!TryParseArguments(args, out var targetPath, out var filePath, out var dryRun))
        {
            await errors.WriteLineAsync(Usage);
            return 2;
        }

        try
        {
            var target = LauncherProfileLoader.LoadSource(targetPath, providers);
            var storage = providers.GetStorage(target.Type);
            var reader = storage.CreateReader(target.Settings);
            var writer = storage.CreateWriter(target.Settings);
            // Disposed on every exit path. The writer is declared first so the reader is disposed first, as before.
            using var writerDisposal = writer as IDisposable;
            using var readerDisposal = reader as IDisposable;

            var file = new FileInfo(filePath);
            var size = file.Exists ? file.Length : 0;
            var publisher = new ReleasePublisher(reader, writer, ReleaseFileNaming.Default);
            var result = await publisher.PublishAsync(filePath, dryRun, UploadProgress(output, size), cancellationToken);
            await WriteResultAsync(result, target.Type);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await errors.WriteLineAsync("Canceled.");
            return 1;
        }
        catch (Exception error) when (error is ReleaseStoreException or IOException or InvalidDataException
            or InvalidOperationException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            await errors.WriteLineAsync(error.Message);
            return 1;
        }
    }

    private async Task WriteResultAsync(PublishResult result, string storeType)
    {
        if (result.Uploaded is { } uploaded)
        {
            var checksum = uploaded.Checksum is { } c ? $" ({c.Algorithm} {c.Value})" : "";
            await output.WriteLineAsync($"Published version {result.Version} as {uploaded.Name}, id {uploaded.Key}{checksum}.");
        }
        else
        {
            await output.WriteLineAsync($"Dry run: version {result.Version} isn't published yet and would be uploaded to {storeType}.");
        }
    }

    private static bool TryParseArguments(string[] args, out string target, out string file, out bool dryRun)
    {
        target = file = "";
        dryRun = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--target" when i + 1 < args.Length && target.Length == 0:
                    target = args[++i];
                    break;
                case "--file" when i + 1 < args.Length && file.Length == 0:
                    file = args[++i];
                    break;
                case "--dry-run" when !dryRun:
                    dryRun = true;
                    break;
                default:
                    return false;
            }
        }

        return target.Length != 0 && file.Length != 0;
    }

    /// <summary>Prints upload progress in 10% steps.</summary>
    private static IProgress<long> UploadProgress(TextWriter output, long total)
    {
        var lastStep = -1;
        return new ActionProgress<long>(bytes =>
        {
            var step = total > 0 ? (int)Math.Min(10, bytes * 10 / total) : 10;
            if (step > lastStep)
            {
                lastStep = step;
                output.WriteLine($"Uploaded {step * 10}%");
            }
        });
    }
}
