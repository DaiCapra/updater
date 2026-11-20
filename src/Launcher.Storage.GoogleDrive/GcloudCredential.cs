using System.Diagnostics;
using System.Net.Http.Headers;
using Google.Apis.Http;

namespace Launcher.Storage.GoogleDrive;

/// <summary>
/// Authenticates Drive requests as the user signed in to the gcloud CLI
/// (<c>gcloud auth login --enable-gdrive-access</c>). Files uploaded this way use that user's Drive storage,
/// which service accounts don't have. Tokens are fetched from gcloud and refreshed before they expire.
/// </summary>
internal sealed class GcloudCredential(Func<CancellationToken, Task<string>>? fetchToken = null, TimeProvider? clock = null)
    : IConfigurableHttpClientInitializer, IHttpExecuteInterceptor
{
    // gcloud access tokens last an hour; refresh well before that so long uploads keep working.
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(45);

    public const string AccountLabel = "Your gcloud account";

    private readonly Func<CancellationToken, Task<string>> fetchToken = fetchToken ?? PrintAccessTokenAsync;
    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private readonly SemaphoreSlim gate = new(1, 1);
    private string? token;
    private DateTimeOffset expires;

    public void Initialize(ConfigurableHttpClient httpClient) => httpClient.MessageHandler.AddExecuteInterceptor(this);

    public async Task InterceptAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync(cancellationToken).ConfigureAwait(false));
    }

    private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (token is null || clock.GetUtcNow() >= expires)
            {
                token = await fetchToken(cancellationToken).ConfigureAwait(false);
                expires = clock.GetUtcNow() + Lifetime;
            }

            return token;
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task<string> PrintAccessTokenAsync(CancellationToken cancellationToken)
    {
        // gcloud is a .cmd script on Windows, so it has to be started through cmd.exe.
        var (fileName, arguments) = OperatingSystem.IsWindows()
            ? ("cmd.exe", $"/d /c \"\"{FindWindowsGcloud()}\" auth print-access-token\"")
            : ("gcloud", "auth print-access-token");

        using var process = Process.Start(new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidDataException("Couldn't start gcloud.");

        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errors = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        var accessToken = (await output.ConfigureAwait(false)).Trim();
        if (process.ExitCode != 0 || accessToken.Length == 0)
        {
            var details = (await errors.ConfigureAwait(false)).Trim();
            throw new InvalidDataException(
                ("Couldn't get a Google token from gcloud. Install the Google Cloud CLI and run " +
                 $"'gcloud auth login --enable-gdrive-access'. {details}").TrimEnd());
        }

        return accessToken;
    }

    /// <summary>gcloud on PATH, else the installer's default folders (a terminal opened before installing has no PATH entry).</summary>
    private static string FindWindowsGcloud()
    {
        var onPath = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory.Trim('"'), "gcloud.cmd"));
        var installed = new[] { Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles }
            .Select(folder => Path.Combine(Environment.GetFolderPath(folder), "Google", "Cloud SDK", "google-cloud-sdk", "bin", "gcloud.cmd"));
        return onPath.Concat(installed).FirstOrDefault(File.Exists) ?? "gcloud";
    }
}
