using System.Text.Json;
using Launcher.Core;
using Launcher.Services.Providers;

namespace Launcher.Services;

public sealed record HttpJsonSettings(Uri Url) : ISourceSettings;

/// <summary>The <c>http-json</c> source: an HTTPS manifest with a version and a download page link.</summary>
public sealed class HttpJsonSourceFactory : IUpdateSourceFactory
{
    public const string TypeName = "http-json";

    public string Type => TypeName;

    public ISourceSettings Parse(JsonElement json, string baseDirectory) =>
        new HttpJsonSettings(LauncherJson.ParseHttpsUrl(SettingsJson.RequireString(json, "url", TypeName), "url"));

    public IUpdateSource Create(ISourceSettings settings)
    {
        var http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15),
            MaxResponseContentBufferSize = 64 * 1024
        };
        return new HttpJsonUpdateSource(http, ((HttpJsonSettings)settings).Url, ownsClient: true);
    }
}

public sealed class HttpJsonUpdateSource(HttpClient httpClient, Uri feedUrl, bool ownsClient = false) : IUpdateSource, IDisposable
{
    public async Task<UpdateRelease?> GetLatestReleaseAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(feedUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var release = await JsonSerializer
            .DeserializeAsync<ReleaseData>(stream, LauncherJson.Options, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException("The update feed must contain a release object.");

        var releaseVersion = LauncherJson.ParseVersion(release.Version, "version");
        var downloadUrl = LauncherJson.ParseHttpsUrl(release.DownloadUrl, "downloadUrl");
        return new UpdateRelease(releaseVersion, DownloadPage: downloadUrl);
    }

    public void Dispose()
    {
        if (ownsClient)
        {
            httpClient.Dispose();
        }
    }

    private sealed record ReleaseData(string? Version, string? DownloadUrl);
}
