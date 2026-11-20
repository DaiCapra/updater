using System.IO;
using Launcher.Core;
using Launcher.Core.Releases;
using Launcher.Services;
using Launcher.Services.Providers;
using Launcher.Storage.GoogleDrive;
using Launcher.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Launcher.Tests;

public class ProviderTests
{
    [Fact]
    public void DriveProfileParsesFolderAndResolvesCredentialsBesideTheProfile()
    {
        using var temp = new TempDirectory();
        var profilePath = temp.Write("profiles/game.json", """
            {
                "displayName": "Example Game",
                "installedVersion": "0.0.21",
                "updateSource": { "type": "google-drive", "folderId": "1AbC-d_E", "credentials": "keys/download.json" }
            }
            """);

        var profile = LauncherProfileLoader.Load(["--profile", profilePath], AppContext.BaseDirectory, App.CreateProviders());

        Assert.Equal(GoogleDriveStorageProvider.TypeName, profile.UpdateSource?.Type);
        Assert.Equal(
            new GoogleDriveSettings("1AbC-d_E", temp.Combine("profiles", "keys", "download.json")),
            profile.UpdateSource?.Settings);
        Assert.Equal(new Version(0, 0, 21), profile.InstalledVersion);
    }

    [Fact]
    public void HttpJsonProfileUsesTheUpdateSourceObject()
    {
        using var temp = new TempDirectory();
        var profilePath = temp.Write("game.json", """
            { "installedVersion": "1.0", "updateSource": { "type": "http-json", "url": "https://example.com/latest.json" } }
            """);

        var profile = LauncherProfileLoader.Load(["--profile", profilePath], AppContext.BaseDirectory, App.CreateProviders());

        Assert.Equal(new HttpJsonSettings(new Uri("https://example.com/latest.json")), profile.UpdateSource?.Settings);
    }

    [Theory]
    [InlineData("""{ "installedVersion": "1.0", "updateFeedUrl": "https://example.com/a.json", "updateSource": { "type": "http-json", "url": "https://example.com/b.json" } }""")]
    [InlineData("""{ "installedVersion": "1.0", "updateSource": { "type": "ftp" } }""")]
    [InlineData("""{ "installedVersion": "1.0", "updateSource": { "folderId": "abc" } }""")]
    [InlineData("""{ "installedVersion": "1.0", "updateSource": "google-drive" }""")]
    [InlineData("""{ "installedVersion": "1.0", "updateSource": { "type": "google-drive", "credentials": "key.json" } }""")]
    [InlineData("""{ "installedVersion": "1.0", "updateSource": { "type": "google-drive", "folderId": "abc" } }""")]
    [InlineData("""{ "installedVersion": "1.0", "updateSource": { "type": "google-drive", "folderId": "abc' or '1", "credentials": "key.json" } }""")]
    [InlineData("""{ "installedVersion": "1.0", "updateSource": { "type": "http-json", "url": "http://example.com/latest.json" } }""")]
    [InlineData("""{ "updateSource": { "type": "google-drive", "folderId": "abc", "credentials": "key.json" } }""")]
    public void InvalidUpdateSourcesAreConfigurationErrors(string json)
    {
        using var temp = new TempDirectory();
        var profilePath = temp.Write("game.json", json);

        Assert.Throws<InvalidDataException>(
            () => LauncherProfileLoader.Load(["--profile", profilePath], AppContext.BaseDirectory, App.CreateProviders()));
    }

    [Fact]
    public void RegistryRejectsTwoProvidersForOneType()
    {
        Assert.Throws<ArgumentException>(() => new ProviderRegistry(
            [new GoogleDriveStorageProvider(), new GoogleDriveStorageProvider()], []));
    }

    [Fact]
    public void ManifestSourcesCannotBePublishedTo()
    {
        Assert.Throws<InvalidDataException>(() => App.CreateProviders().GetStorage(HttpJsonSourceFactory.TypeName));
    }

    [Fact]
    public async Task AnyRegisteredStorageProviderGetsTheStoreSourceAndDownloader()
    {
        using var temp = new TempDirectory();
        var store = new InMemoryReleaseStore();
        var file = store.Add("build-2.0.zip", [1, 2, 3]);
        var providers = new ProviderRegistry([new InMemoryStorageProvider(store)], [new HttpJsonSourceFactory()]);
        var profilePath = temp.Write("game.json", """
            { "installedVersion": "1.0", "updateSource": { "type": "memory", "name": "test" } }
            """);
        var profile = LauncherProfileLoader.Load(["--profile", profilePath], AppContext.BaseDirectory, providers);

        using var services = App.CreateServices(profile, providers, temp.Path);
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        var release = await viewModel.CheckForUpdatesAsync();

        Assert.NotNull(release);
        Assert.Equal(file, release.File);
        Assert.True(viewModel.CanDownload(release));
        var path = await viewModel.DownloadAsync(release, new Progress<double>(), default);
        Assert.StartsWith(temp.Path, path);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(path));
    }

    [Fact]
    public void ManifestSourcesOpenTheDownloadPageInsteadOfDownloading()
    {
        var profile = new LauncherProfile("Game",
            UpdateSource: new UpdateSourceConfig(HttpJsonSourceFactory.TypeName, new HttpJsonSettings(new Uri("https://example.com/a.json"))),
            InstalledVersion: new Version(1, 0));

        using var services = App.CreateServices(profile);
        var viewModel = services.GetRequiredService<MainWindowViewModel>();

        Assert.IsType<HttpJsonUpdateSource>(services.GetRequiredService<IUpdateSource>());
        Assert.Null(services.GetService<IReleaseDownloader>());
        Assert.False(viewModel.CanDownload(new UpdateRelease(new Version(2, 0), new Uri("https://example.com/setup"))));
    }
}
