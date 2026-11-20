using System.IO;
using System.Text;
using Launcher.Core;
using Launcher.Core.Releases;
using Launcher.Core.Storage;
using Launcher.Tests.Fakes;
using Xunit;

namespace Launcher.Tests;

public class ReleaseTests
{
    [Theory]
    [InlineData("build-1.2.zip", "1.2")]
    [InlineData("BUILD-0.0.22.ZIP", "0.0.22")]
    [InlineData("build-1.2.3.4.zip", "1.2.3.4")]
    [InlineData("build-1.zip", null)]
    [InlineData("build-1.2-beta.zip", null)]
    [InlineData("build- 1.2.zip", null)]
    [InlineData("build-.zip", null)]
    [InlineData("release-1.2.zip", null)]
    [InlineData("build-1.2.zip.partial", null)]
    public void NamingParsesOnlyNumericBuildNames(string name, string? expected)
    {
        var parsed = ReleaseFileNaming.Default.TryParse(name, out var version);

        Assert.Equal(expected is not null, parsed);
        Assert.Equal(expected is null ? null : Version.Parse(expected), version);
    }

    [Fact]
    public async Task StoreSourcePicksTheHighestVersionAndIgnoresOtherFiles()
    {
        var store = new InMemoryReleaseStore();
        store.Add("build-1.9.0.zip", [1]);
        var latest = store.Add("build-1.10.zip", [2]);
        store.Add("build-2.0.0.zip.partial", [3]);
        store.Add("notes.txt", [4]);

        var release = await new StoreUpdateSource(store, ReleaseFileNaming.Default).GetLatestReleaseAsync(default);

        Assert.NotNull(release);
        Assert.Equal(new Version(1, 10), release.Version);
        Assert.Equal(latest, release.File);
        Assert.Null(release.DownloadPage);
    }

    [Fact]
    public async Task StoreSourceWithoutReleaseFilesReturnsNull()
    {
        var store = new InMemoryReleaseStore();
        store.Add("readme.md", [1]);

        Assert.Null(await new StoreUpdateSource(store, ReleaseFileNaming.Default).GetLatestReleaseAsync(default));
    }

    [Fact]
    public async Task StoreSourceRejectsTwoFilesForTheSameVersion()
    {
        var store = new InMemoryReleaseStore();
        store.Add("build-1.2.zip", [1]);
        store.Add("build-1.2.0.zip", [2]);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => new StoreUpdateSource(store, ReleaseFileNaming.Default).GetLatestReleaseAsync(default));
    }

    [Fact]
    public async Task StoreSourceWorksWithTheUpdateChecker()
    {
        var store = new InMemoryReleaseStore();
        store.Add("build-1.2.zip", [1]);
        var checker = new UpdateChecker(new StoreUpdateSource(store, ReleaseFileNaming.Default));

        Assert.Null(await checker.CheckAsync(new Version(1, 2, 0)));
        Assert.NotNull(await checker.CheckAsync(new Version(1, 1, 9)));
    }

    [Fact]
    public async Task DownloaderVerifiesTheFileAndRenamesItIntoStaging()
    {
        using var temp = new TempDirectory();
        var store = new InMemoryReleaseStore();
        var content = Encoding.UTF8.GetBytes("the release build content");
        var file = store.Add("build-1.0.zip", content);
        var reports = new List<double>();

        var path = await Download(store, temp.Combine("staging"), file, new SyncProgress<double>(reports.Add));

        Assert.Equal(temp.Combine("staging", "build-1.0.zip"), path);
        Assert.Equal(content, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".partial"));
        Assert.Equal(1, reports.Last());
        Assert.All(reports, fraction => Assert.InRange(fraction, 0, 1));
    }

    [Fact]
    public async Task DownloaderReusesAVerifiedFileWithoutDownloadingAgain()
    {
        using var temp = new TempDirectory();
        var store = new InMemoryReleaseStore();
        var file = store.Add("build-1.0.zip", [1, 2, 3]);

        await Download(store, temp.Path, file);
        await Download(store, temp.Path, file);

        Assert.Equal(1, store.Downloads);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task DownloaderDeletesThePartialFileWhenSizeOrChecksumDoNotMatch(bool wrongChecksum, bool wrongSize)
    {
        using var temp = new TempDirectory();
        var store = new InMemoryReleaseStore();
        var file = store.Add("build-1.0.zip", [1, 2, 3],
            reportedChecksum: wrongChecksum ? new FileChecksum("md5", new string('0', 32)) : null,
            reportedSize: wrongSize ? 4 : null);

        await Assert.ThrowsAsync<InvalidDataException>(() => Download(store, temp.Path, file));

        Assert.Empty(Directory.GetFiles(temp.Path));
    }

    [Fact]
    public async Task DownloaderReplacesAStagedFileThatNoLongerVerifies()
    {
        using var temp = new TempDirectory();
        var store = new InMemoryReleaseStore();
        var file = store.Add("build-1.0.zip", [1, 2, 3]);
        File.WriteAllBytes(temp.Combine("build-1.0.zip"), [9, 9, 9]);

        var path = await Download(store, temp.Path, file);

        Assert.Equal([1, 2, 3], File.ReadAllBytes(path));
    }

    [Fact]
    public async Task CanceledDownloadLeavesNoPartialFile()
    {
        using var temp = new TempDirectory();
        var store = new InMemoryReleaseStore();
        var file = store.Add("build-1.0.zip", new byte[64]);
        using var cancellation = new CancellationTokenSource();
        var progress = new SyncProgress<double>(_ => cancellation.Cancel());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Download(store, temp.Path, file, progress, cancellation.Token));

        Assert.Empty(Directory.GetFiles(temp.Path));
    }

    [Fact]
    public async Task DownloaderRejectsUnknownChecksumAlgorithmsBeforeDownloading()
    {
        using var temp = new TempDirectory();
        var store = new InMemoryReleaseStore();
        var file = store.Add("build-1.0.zip", [1], reportedChecksum: new FileChecksum("crc32", "00000000"));

        await Assert.ThrowsAsync<NotSupportedException>(() => Download(store, temp.Path, file));
        Assert.Equal(0, store.Downloads);
    }

    [Theory]
    [InlineData("../build-1.0.zip")]
    [InlineData("sub\\build-1.0.zip")]
    public async Task DownloaderRejectsFileNamesThatEscapeStaging(string name)
    {
        using var temp = new TempDirectory();
        var file = new StoredFile("id", name, 1, null);

        await Assert.ThrowsAsync<InvalidDataException>(() => Download(new InMemoryReleaseStore(), temp.Path, file));
    }

    [Fact]
    public async Task PublisherUploadsANewVersionAndVerifiesIt()
    {
        using var temp = new TempDirectory();
        var path = temp.Write("build-1.3.0.zip", "zip bytes");
        var store = new InMemoryReleaseStore();

        var result = await Publisher(store).PublishAsync(path, false, null, default);

        Assert.Equal(new Version(1, 3, 0), result.Version);
        Assert.Equal("build-1.3.0.zip", result.Uploaded?.Name);
        Assert.Single(await store.ListAsync(default));
    }

    [Fact]
    public async Task PublisherDryRunChecksWithoutUploading()
    {
        using var temp = new TempDirectory();
        var path = temp.Write("build-1.3.0.zip", "zip bytes");
        var store = new InMemoryReleaseStore();

        var result = await Publisher(store).PublishAsync(path, true, null, default);

        Assert.Null(result.Uploaded);
        Assert.Equal(0, store.Uploads);
    }

    [Fact]
    public async Task PublisherRefusesAVersionThatIsAlreadyPublished()
    {
        using var temp = new TempDirectory();
        var path = temp.Write("build-1.3.zip", "zip bytes");
        var store = new InMemoryReleaseStore();
        store.Add("build-1.3.0.zip", [1]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Publisher(store).PublishAsync(path, false, null, default));
        Assert.Equal(0, store.Uploads);
    }

    [Fact]
    public async Task PublisherRejectsFilesThatDoNotFollowTheNaming()
    {
        using var temp = new TempDirectory();
        var path = temp.Write("game.zip", "zip bytes");
        var store = new InMemoryReleaseStore();

        await Assert.ThrowsAsync<InvalidDataException>(() => Publisher(store).PublishAsync(path, false, null, default));
    }

    [Fact]
    public async Task PublisherReportsAnUploadWhoseChecksumDoesNotMatch()
    {
        using var temp = new TempDirectory();
        var path = temp.Write("build-1.3.0.zip", "zip bytes");
        var store = new InMemoryReleaseStore();

        await Assert.ThrowsAsync<InvalidDataException>(
            () => new ReleasePublisher(store, new CorruptingWriter(), ReleaseFileNaming.Default).PublishAsync(path, false, null, default));
    }

    private static UpdateRelease Release(StoredFile file) => new(new Version(1, 0), File: file);

    private static Task<string> Download(InMemoryReleaseStore store, string directory, StoredFile file, IProgress<double>? progress = null, CancellationToken cancellationToken = default) =>
        new StoreReleaseDownloader(store, directory).DownloadAsync(Release(file), progress, cancellationToken);

    private static ReleasePublisher Publisher(InMemoryReleaseStore store) => new(store, store, ReleaseFileNaming.Default);

    private sealed class CorruptingWriter : IReleaseStoreWriter
    {
        public Task<StoredFile> UploadAsync(string name, Stream content, IProgress<long>? progress, CancellationToken cancellationToken) =>
            Task.FromResult(new StoredFile("id", name, content.Length, new FileChecksum("md5", new string('0', 32))));

        public Task DeleteAsync(StoredFile file, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
