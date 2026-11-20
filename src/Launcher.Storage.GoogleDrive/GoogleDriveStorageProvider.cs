using System.Text.Json;
using System.Text.RegularExpressions;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Launcher.Core;
using Launcher.Core.Storage;
using Launcher.Services.Providers;

namespace Launcher.Storage.GoogleDrive;

/// <summary>A Drive folder (in My Drive or a shared drive) accessed with a service-account key.</summary>
public sealed record GoogleDriveSettings(string FolderId, string CredentialsPath) : ISourceSettings;

/// <summary>The <c>google-drive</c> provider. Profile fields: <c>folderId</c>, and <c>credentials</c> for the key file.</summary>
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

        return new GoogleDriveSettings(folderId, SettingsJson.RequirePath(json, "credentials", TypeName, baseDirectory));
    }

    public IReleaseStoreReader CreateReader(ISourceSettings settings) =>
        CreateStore(settings, DriveService.ScopeConstants.DriveReadonly);

    public IReleaseStoreWriter CreateWriter(ISourceSettings settings) =>
        CreateStore(settings, DriveService.ScopeConstants.Drive);

    private static GoogleDriveStore CreateStore(ISourceSettings settings, string scope)
    {
        var drive = (GoogleDriveSettings)settings;
        var (service, account) = CreateService(drive.CredentialsPath, scope);
        return new GoogleDriveStore(service, drive.FolderId, account);
    }

    private static (DriveService Service, string Account) CreateService(string credentialsPath, string scope)
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

        var service = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential.ToGoogleCredential().CreateScoped(scope),
            ApplicationName = "Launcher"
        });
        return (service, credential.Id);
    }

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex DriveId();
}
