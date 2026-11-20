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

        var profile = LoadProfile(profilePath);

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

        var profile = LoadProfile(profilePath);

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
    [InlineData("""{ "installId": "..\\other" }""")]
    [InlineData("""{ "installId": "" }""")]
    [InlineData("""{ "executable": "C:\\Windows\\notepad.exe" }""")]
    public void InvalidUpdateSourcesAreConfigurationErrors(string json)
    {
        using var temp = new TempDirectory();
        var profilePath = temp.Write("game.json", json);

        Assert.Throws<InvalidDataException>(
            () => LoadProfile(profilePath));
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
    public void ProfileKeepsInstallSettings()
    {
        using var temp = new TempDirectory();
        var profilePath = temp.Write("game.json", """
            { "displayName": "Erden: Stone?", "executable": "Bin/Game.exe" }
            """);

        var profile = LoadProfile(profilePath);

        Assert.Equal("Bin/Game.exe", profile.Executable);
        Assert.Null(profile.InstalledVersion);
        Assert.Equal("Erden Stone", profile.InstallFolderName);
        Assert.Equal("custom", (profile with { InstallId = "custom" }).InstallFolderName);
    }

    [Fact]
    public async Task AnyRegisteredStorageProviderCanBeInstalledAndPlayed()
    {
        using var temp = new TempDirectory();
        var store = new InMemoryReleaseStore();
        store.Add("build-2.0.zip", Zips.Create(("Game.exe", "game")));
        var providers = new ProviderRegistry([new InMemoryStorageProvider(store)], [new HttpJsonSourceFactory()]);
        var profilePath = temp.Write("game.json", """{ "updateSource": { "type": "memory", "name": "test" } }""");
        var profile = LauncherProfileLoader.Load(["--profile", profilePath], AppContext.BaseDirectory, providers);
        var shell = new FakeShell();

        using var services = App.CreateServices(profile, providers, temp.Combine("install"), shell);
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        await viewModel.CheckForUpdatesAsync();
        await viewModel.InstallUpdateAsync();
        viewModel.Play();

        Assert.Equal(new Version(2, 0), viewModel.InstalledVersion);
        Assert.Equal(temp.Combine("install", "versions", "2.0", "Game.exe"), Assert.Single(shell.Apps));
    }

    [Fact]
    public async Task ManifestSourcesOpenTheDownloadPageInsteadOfInstalling()
    {
        var profile = new LauncherProfile("Game",
            UpdateSource: new UpdateSourceConfig("manifest", new HttpJsonSettings(new Uri("https://example.com/a.json"))),
            InstalledVersion: new Version(1, 0));
        var page = new Uri("https://example.com/setup");
        var providers = new ProviderRegistry([], [new FixedSourceFactory(new UpdateRelease(new Version(2, 0), page))]);
        var shell = new FakeShell();
        using var temp = new TempDirectory();

        using var services = App.CreateServices(profile, providers, temp.Path, shell);
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        await viewModel.CheckForUpdatesAsync();

        Assert.Null(services.GetService<IReleaseDownloader>());
        Assert.Equal("Open download", viewModel.InstallText);
        await viewModel.InstallUpdateAsync();
        Assert.Equal(page, Assert.Single(shell.Urls));
    }

    private static LauncherProfile LoadProfile(string profilePath) =>
        LauncherProfileLoader.Load(["--profile", profilePath], AppContext.BaseDirectory, App.CreateProviders());

    private sealed class FixedSourceFactory(UpdateRelease release) : IUpdateSourceFactory, IUpdateSource
    {
        public string Type => "manifest";

        public ISourceSettings Parse(System.Text.Json.JsonElement json, string baseDirectory) => throw new NotSupportedException();

        public IUpdateSource Create(ISourceSettings settings) => this;

        public Task<UpdateRelease?> GetLatestReleaseAsync(CancellationToken cancellationToken) => Task.FromResult<UpdateRelease?>(release);
    }
}
