using System.IO;
using System.Net;
using System.Net.Http;
using Launcher.Core;
using Launcher.Services;
using Xunit;

namespace Launcher.Tests;

public class ProfileAndUpdateTests
{
    [Fact]
    public void ExplicitProfileControlsBrandingAndResolvesAssetsBesideTheProfile()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var profilePath = Path.Combine(directory.FullName, "example.json");
            File.WriteAllText(profilePath, """
                {
                    "displayName": "Example Studio",
                    "backgroundImage": "art/background.jpg",
                    "updateFeedUrl": "https://example.com/releases/latest.json",
                    "installedVersion": "1.2.0"
                }
                """);

            var profile = LauncherProfileLoader.Load(["--profile", profilePath], AppContext.BaseDirectory);
            using var services = App.CreateServices(profile);

            var viewModel = Assert.IsType<MainWindowViewModel>(services.GetService(typeof(MainWindowViewModel)));
            Assert.Equal("Example Studio", viewModel.Title);
            Assert.Equal(Path.Combine(directory.FullName, "art", "background.jpg"), viewModel.BackgroundPath);
            Assert.True(viewModel.CanCheckForUpdates);
            Assert.Equal(new Uri("https://example.com/releases/latest.json"), profile.UpdateFeedUrl);
            Assert.Equal(new Version(1, 2, 0), profile.InstalledVersion);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("1.10.0", true)]
    [InlineData("1.2", false)]
    [InlineData("1.1.9", false)]
    public async Task FeedReportsOnlyNewerVersions(string releaseVersion, bool expectedUpdate)
    {
        using var http = new HttpClient(new FeedHandler(releaseVersion, "https://example.com/setup.exe"));
        var source = new HttpJsonUpdateSource(http, new Uri("https://example.com/latest.json"));
        var checker = new UpdateChecker(source);

        var update = await checker.CheckAsync(new Version(1, 2, 0));

        if (expectedUpdate)
        {
            Assert.NotNull(update);
            Assert.Equal(Version.Parse(releaseVersion), update.Version);
            Assert.Equal(new Uri("https://example.com/setup.exe"), update.DownloadUrl);
        }
        else
        {
            Assert.Null(update);
        }
    }

    [Fact]
    public async Task FeedRejectsNonHttpsDownloadLinks()
    {
        using var http = new HttpClient(new FeedHandler("2.0", "file:///C:/setup.exe"));
        var source = new HttpJsonUpdateSource(http, new Uri("https://example.com/latest.json"));
        var checker = new UpdateChecker(source);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => checker.CheckAsync(new Version(1, 0)));
    }

    private sealed class FeedHandler(string version, string downloadUrl) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(new Uri("https://example.com/latest.json"), request.RequestUri);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(new { version, downloadUrl }))
            });
        }
    }
}
