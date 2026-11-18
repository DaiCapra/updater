using System.Text.Json;
using Launcher.Core;

namespace Launcher.Services;

public sealed class HttpJsonUpdateSource(HttpClient httpClient, Uri feedUrl) : IUpdateSource
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
        return new UpdateRelease(releaseVersion, downloadUrl);
    }

    private sealed record ReleaseData(string? Version, string? DownloadUrl);
}
