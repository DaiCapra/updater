using System.IO;
using System.Security.Cryptography;
using Launcher.Core.Storage;
using Launcher.Services;
using Launcher.Storage.GoogleDrive;
using Launcher.Tests.Fakes;
using Xunit;

namespace Launcher.Tests;

/// <summary>
/// The behavior every release store must share. Each new host (S3, ...) adds a subclass that creates
/// its store; the host-agnostic code relies on exactly these guarantees.
/// </summary>
public abstract class ReleaseStoreContractTests
{
    /// <summary>A reason to skip, such as missing credentials for a live store; null runs the suite.</summary>
    protected virtual string? SkipReason => null;

    private protected abstract (IReleaseStoreReader Reader, IReleaseStoreWriter Writer) CreateStore();

    [SkippableFact]
    public async Task UploadedFileIsListedWithItsSizeAndAChecksumThatMatchesTheContent()
    {
        Skip.If(SkipReason is not null, SkipReason);
        var (reader, writer) = CreateStore();
        var content = RandomNumberGenerator.GetBytes(64 * 1024 + 7);
        var uploaded = await writer.UploadAsync(UniqueName(), new MemoryStream(content), null, default);
        try
        {
            var listed = Assert.Single(await reader.ListAsync(default), file => file.Key == uploaded.Key);
            Assert.Equal(uploaded.Name, listed.Name);
            Assert.Equal(content.Length, listed.Size);
            var checksum = Assert.IsType<FileChecksum>(listed.Checksum);
            Assert.True(await ChecksumAlgorithms.MatchesAsync(new MemoryStream(content), checksum, default));
        }
        finally
        {
            await writer.DeleteAsync(uploaded, default);
        }
    }

    [SkippableFact]
    public async Task DownloadReturnsTheUploadedBytesAndReportsProgressUpToTheFullSize()
    {
        Skip.If(SkipReason is not null, SkipReason);
        var (reader, writer) = CreateStore();
        var content = RandomNumberGenerator.GetBytes(32 * 1024);
        var uploaded = await writer.UploadAsync(UniqueName(), new MemoryStream(content), null, default);
        try
        {
            var reports = new List<long>();
            using var destination = new MemoryStream();
            await reader.DownloadAsync(uploaded, destination, new RecordingProgress(reports), default);

            Assert.Equal(content, destination.ToArray());
            Assert.Equal(content.Length, reports.Last());
        }
        finally
        {
            await writer.DeleteAsync(uploaded, default);
        }
    }

    [SkippableFact]
    public async Task DeletedFileIsNoLongerListed()
    {
        Skip.If(SkipReason is not null, SkipReason);
        var (reader, writer) = CreateStore();
        var uploaded = await writer.UploadAsync(UniqueName(), new MemoryStream([1, 2, 3]), null, default);

        await writer.DeleteAsync(uploaded, default);

        Assert.DoesNotContain(await reader.ListAsync(default), file => file.Key == uploaded.Key);
    }

    [SkippableFact]
    public async Task DownloadingAMissingFileIsReportedAsNotFound()
    {
        Skip.If(SkipReason is not null, SkipReason);
        var (reader, _) = CreateStore();
        var missing = new StoredFile("missing0000000000000000000000000", "missing.zip", 1, null);

        var error = await Assert.ThrowsAsync<ReleaseStoreException>(
            () => reader.DownloadAsync(missing, new MemoryStream(), null, default));
        Assert.Equal(ReleaseStoreErrorKind.NotFound, error.Kind);
    }

    [SkippableFact]
    public async Task ListingWithACanceledTokenThrowsOperationCanceled()
    {
        Skip.If(SkipReason is not null, SkipReason);
        var (reader, _) = CreateStore();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ListAsync(new CancellationToken(canceled: true)));
    }

    private static string UniqueName() => $"contract-{Guid.NewGuid():N}.bin";

    private sealed class RecordingProgress(List<long> reports) : IProgress<long>
    {
        public void Report(long value) => reports.Add(value);
    }
}

public sealed class InMemoryReleaseStoreContractTests : ReleaseStoreContractTests
{
    private protected override (IReleaseStoreReader Reader, IReleaseStoreWriter Writer) CreateStore()
    {
        var store = new InMemoryReleaseStore();
        return (store, store);
    }
}

/// <summary>
/// Runs the contract against a real Drive folder. Set LAUNCHER_DRIVE_TEST_TARGET to a target JSON
/// (<c>{ "type": "google-drive", "folderId": ..., "credentials": ... }</c>) whose key can upload.
/// </summary>
public sealed class GoogleDriveReleaseStoreContractTests : ReleaseStoreContractTests
{
    private const string TargetVariable = "LAUNCHER_DRIVE_TEST_TARGET";

    protected override string? SkipReason =>
        Environment.GetEnvironmentVariable(TargetVariable) is { Length: > 0 }
            ? null
            : $"Set {TargetVariable} to a google-drive target JSON to run the live Drive contract tests.";

    private protected override (IReleaseStoreReader Reader, IReleaseStoreWriter Writer) CreateStore()
    {
        var providers = App.CreateProviders();
        var target = LauncherProfileLoader.LoadSource(Environment.GetEnvironmentVariable(TargetVariable)!, providers);
        Assert.Equal(GoogleDriveStorageProvider.TypeName, target.Type);
        var provider = providers.GetStorage(target.Type);
        return (provider.CreateReader(target.Settings), provider.CreateWriter(target.Settings));
    }
}
