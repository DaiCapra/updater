using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using Google;
using Google.Apis.Auth.OAuth2.Responses;
using Launcher.Core;
using Launcher.Core.Releases;
using Launcher.Core.Storage;
using Launcher.Storage.GoogleDrive;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Launcher.Tests;

public class GoogleDriveTests
{
    [Fact]
    public void DriveProfileResolvesARealDriveReaderFromAServiceAccountKey()
    {
        using var temp = new TempDirectory();
        var keyPath = WriteServiceAccountKey(temp);
        var profile = new LauncherProfile("Game",
            UpdateSource: new UpdateSourceConfig(GoogleDriveStorageProvider.TypeName, new GoogleDriveSettings("folder", keyPath)),
            InstalledVersion: new Version(1, 0));

        using var services = App.CreateServices(profile, downloadsRoot: temp.Path);

        Assert.IsType<StoreUpdateSource>(services.GetRequiredService<IUpdateSource>());
        Assert.IsType<StoreReleaseDownloader>(services.GetRequiredService<IReleaseDownloader>());
        Assert.Null(services.GetService<IReleaseStoreWriter>());
    }

    [Theory]
    [InlineData("""{ "type": "authorized_user", "client_id": "a", "client_secret": "b", "refresh_token": "c" }""")]
    [InlineData("not json")]
    public void DriveRejectsKeysThatAreNotServiceAccounts(string key)
    {
        using var temp = new TempDirectory();
        var settings = new GoogleDriveSettings("folder", temp.Write("key.json", key));

        Assert.Throws<InvalidDataException>(() => new GoogleDriveStorageProvider().CreateReader(settings));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, ReleaseStoreErrorKind.AccessDenied)]
    [InlineData(HttpStatusCode.Unauthorized, ReleaseStoreErrorKind.AccessDenied)]
    [InlineData(HttpStatusCode.NotFound, ReleaseStoreErrorKind.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError, ReleaseStoreErrorKind.Unavailable)]
    public void DriveApiErrorsBecomeStoreErrorsThatNameTheAccount(HttpStatusCode status, ReleaseStoreErrorKind kind)
    {
        var errors = new GoogleDriveErrors("folder123", "reader@example.iam.gserviceaccount.com");

        var error = Assert.IsType<ReleaseStoreException>(
            errors.Translate(new GoogleApiException("drive", "failed") { HttpStatusCode = status }));

        Assert.Equal(kind, error.Kind);
        if (kind != ReleaseStoreErrorKind.Unavailable)
        {
            Assert.Contains("reader@example.iam.gserviceaccount.com", error.Message);
        }
    }

    [Fact]
    public void RejectedKeysAndNetworkFailuresBecomeStoreErrors()
    {
        var errors = new GoogleDriveErrors("folder123", "reader@example.iam.gserviceaccount.com");

        var rejected = Assert.IsType<ReleaseStoreException>(
            errors.Translate(new TokenResponseException(new TokenErrorResponse { Error = "invalid_grant" })));
        var offline = Assert.IsType<ReleaseStoreException>(errors.Translate(new HttpRequestException("offline")));

        Assert.Equal(ReleaseStoreErrorKind.AccessDenied, rejected.Kind);
        Assert.Equal(ReleaseStoreErrorKind.Unavailable, offline.Kind);
        Assert.IsType<OperationCanceledException>(errors.Translate(new OperationCanceledException()));
    }

    private static string WriteServiceAccountKey(TempDirectory temp)
    {
        using var rsa = RSA.Create(2048);
        return temp.Write("key.json", JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["type"] = "service_account",
            ["project_id"] = "test-project",
            ["private_key_id"] = "test-key",
            ["private_key"] = rsa.ExportPkcs8PrivateKeyPem(),
            ["client_email"] = "reader@test-project.iam.gserviceaccount.com",
            ["client_id"] = "1",
            ["token_uri"] = "https://oauth2.googleapis.com/token"
        }));
    }
}
