using System.Text.Json;
using System.Text.RegularExpressions;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Http;
using Google.Apis.Services;
using Launcher.Core;
using Launcher.Core.Storage;
using Launcher.Services.Providers;

namespace Launcher.Storage.GoogleDrive;

public enum GoogleDriveAuth
{
    /// <summary>A service-account key file (<c>credentials</c>). Fine for reading; can't upload to My Drive.</summary>
    ServiceAccount,

    /// <summary>The user signed in to the gcloud CLI. Uploads use that user's Drive storage.</summary>
    Gcloud
}

/// <summary>A Drive folder (in My Drive or a shared drive) and how to authenticate to it.</summary>
public sealed record GoogleDriveSettings(string FolderId, string? CredentialsPath, GoogleDriveAuth Auth = GoogleDriveAuth.ServiceAccount)
    : ISourceSettings;

/// <summary>
/// The <c>google-drive</c> provider. Fields: <c>folderId</c>, and either <c>credentials</c> (a service-account key file)
/// or <c>"auth": "gcloud"</c> to act as the user signed in to the gcloud CLI.
/// </summary>
public sealed partial class GoogleDriveStorageProvider : IStorageProvider
{
    public const string TypeName = "google-drive";

    public string Type => TypeName;

    public ISourceSettings Parse(JsonElement json, string baseDirectory)
    {
        var folderId = SettingsJson.RequireString(json, "folderId", TypeName);
        if (!DriveId().IsMatch(folderId))
        {
            throw new InvalidDataException(
                "The google-drive folderId must be the ID from the folder's URL, which contains only letters, digits, '-' and '_'.");
        }

        var auth = json.TryGetProperty("auth", out var value) ? value.GetString() : null;
        return auth switch
        {
            null or "service-account" => new GoogleDriveSettings(folderId, SettingsJson.RequirePath(json, "credentials", TypeName, baseDirectory)),
            "gcloud" => new GoogleDriveSettings(folderId, null, GoogleDriveAuth.Gcloud),
            _ => throw new InvalidDataException($"The google-drive auth must be \"service-account\" or \"gcloud\", not \"{auth}\".")
        };
    }

    public IReleaseStoreReader CreateReader(ISourceSettings settings) =>
        CreateStore(settings, DriveService.ScopeConstants.DriveReadonly);

    public IReleaseStoreWriter CreateWriter(ISourceSettings settings) =>
        CreateStore(settings, DriveService.ScopeConstants.Drive);

    private static GoogleDriveStore CreateStore(ISourceSettings settings, string scope)
    {
        var drive = (GoogleDriveSettings)settings;
        var (initializer, account) = drive.Auth == GoogleDriveAuth.Gcloud
            ? (new GcloudCredential(), GcloudCredential.AccountLabel)
            : LoadServiceAccount(drive.CredentialsPath!, scope);
        var service = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = initializer,
            ApplicationName = "Launcher"
        });
        return new GoogleDriveStore(service, drive.FolderId, account);
    }

    private static (IConfigurableHttpClientInitializer Initializer, string Account) LoadServiceAccount(string credentialsPath, string scope)
    {
        ServiceAccountCredential credential;
        try
        {
            // Load explicitly as a service account; GoogleCredential.FromJson accepts any credential type.
            credential = CredentialFactory.FromFile<ServiceAccountCredential>(credentialsPath);
        }
        catch (Exception error) when (error is not (IOException or UnauthorizedAccessException))
        {
            throw new InvalidDataException(
                $"The Google service-account key at {credentialsPath} couldn't be loaded. {error.Message}", error);
        }

        return (credential.ToGoogleCredential().CreateScoped(scope), credential.Id);
    }

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex DriveId();
}
