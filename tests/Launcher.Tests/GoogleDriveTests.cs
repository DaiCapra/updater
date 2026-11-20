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

        using var services = App.CreateServices(profile, installRoot: temp.Path);

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

    [Fact]
    public void QuotaAndScopeErrorsExplainTheFix()
    {
        var errors = new GoogleDriveErrors("folder123", "uploader@example.iam.gserviceaccount.com");
        GoogleApiException Forbidden(string reason) => new("drive", "failed")
        {
            HttpStatusCode = HttpStatusCode.Forbidden,
            Error = new Google.Apis.Requests.RequestError { Errors = [new Google.Apis.Requests.SingleError { Reason = reason }] }
        };

        var quota = Assert.IsType<ReleaseStoreException>(errors.Translate(Forbidden("storageQuotaExceeded")));
        var scope = Assert.IsType<ReleaseStoreException>(errors.Translate(Forbidden("insufficientPermissions")));

        Assert.Contains("\"auth\": \"gcloud\"", quota.Message);
        Assert.DoesNotContain("Share the folder", quota.Message);
        Assert.Contains("gcloud auth login --enable-gdrive-access", scope.Message);
    }

    [Theory]
    [InlineData("""{ "folderId": "abc", "auth": "gcloud" }""", GoogleDriveAuth.Gcloud, false)]
    [InlineData("""{ "folderId": "abc", "credentials": "key.json" }""", GoogleDriveAuth.ServiceAccount, true)]
    [InlineData("""{ "folderId": "abc", "auth": "service-account", "credentials": "key.json" }""", GoogleDriveAuth.ServiceAccount, true)]
    public void AuthChoosesBetweenAKeyFileAndGcloud(string json, GoogleDriveAuth auth, bool hasKey)
    {
        var settings = Assert.IsType<GoogleDriveSettings>(
            new GoogleDriveStorageProvider().Parse(JsonDocument.Parse(json).RootElement, @"C:\profiles"));

        Assert.Equal(auth, settings.Auth);
        Assert.Equal(hasKey ? @"C:\profiles\key.json" : null, settings.CredentialsPath);
    }

    [Theory]
    [InlineData("""{ "folderId": "abc", "auth": "oauth" }""")]
    [InlineData("""{ "folderId": "abc", "auth": "service-account" }""")]
    public void InvalidAuthSettingsAreConfigurationErrors(string json)
    {
        Assert.Throws<InvalidDataException>(
            () => new GoogleDriveStorageProvider().Parse(JsonDocument.Parse(json).RootElement, @"C:\profiles"));
    }

    [Fact]
    public async Task GcloudTokensAreCachedAndRefreshedBeforeTheyExpire()
    {
        var clock = new ManualClock();
        var fetches = 0;
        var credential = new GcloudCredential(_ => Task.FromResult($"token-{++fetches}"), clock);
        async Task<string?> Authorize()
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/drive/v3/files");
            await credential.InterceptAsync(request, default);
            return request.Headers.Authorization?.ToString();
        }

        Assert.Equal("Bearer token-1", await Authorize());
        clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal("Bearer token-1", await Authorize());
        clock.Advance(TimeSpan.FromMinutes(20));
        Assert.Equal("Bearer token-2", await Authorize());
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UnixEpoch;

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan by) => now += by;
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
