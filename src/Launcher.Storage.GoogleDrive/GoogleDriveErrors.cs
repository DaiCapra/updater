using System.Net;
using Google;
using Google.Apis.Auth.OAuth2.Responses;
using Launcher.Core.Storage;

namespace Launcher.Storage.GoogleDrive;

/// <summary>Translates Google API failures into <see cref="ReleaseStoreException"/>s with actionable messages.</summary>
internal sealed class GoogleDriveErrors(string folderId, string account)
{
    public Task Guard(Task call, string? item = null) => Guard(AwaitAsValue(call), item);

    public async Task<T> Guard<T>(Task<T> call, string? item = null)
    {
        try
        {
            return await call.ConfigureAwait(false);
        }
        catch (Exception error) when (IsStoreError(error))
        {
            throw Translate(error, item);
        }
    }

    private static async Task<bool> AwaitAsValue(Task call)
    {
        await call.ConfigureAwait(false);
        return true;
    }

    public Exception Translate(Exception? error, string? item = null)
    {
        if (error is OperationCanceledException or ReleaseStoreException)
        {
            return error;
        }

        var target = item is null ? $"the Google Drive folder {folderId}" : $"{item} in Google Drive";
        return error switch
        {
            TokenResponseException => new ReleaseStoreException(ReleaseStoreErrorKind.AccessDenied,
                $"Google rejected the service-account key for {account}. The key may be revoked or expired.", error),
            GoogleApiException { HttpStatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } api
                when !IsRateLimit(api) => new ReleaseStoreException(ReleaseStoreErrorKind.AccessDenied,
                    $"{account} isn't allowed to access {target}. Share the folder with that account. {api.Error?.Message}".TrimEnd(), error),
            GoogleApiException { HttpStatusCode: HttpStatusCode.NotFound } => new ReleaseStoreException(ReleaseStoreErrorKind.NotFound,
                $"Couldn't find {target}. Check the ID and that the folder is shared with {account}.", error),
            GoogleApiException api => new ReleaseStoreException(ReleaseStoreErrorKind.Unavailable,
                $"Google Drive couldn't complete the request for {target}. {api.Error?.Message ?? api.Message}", error),
            HttpRequestException => new ReleaseStoreException(ReleaseStoreErrorKind.Unavailable,
                $"Couldn't reach Google Drive. {error.Message}", error),
            null => new ReleaseStoreException(ReleaseStoreErrorKind.Unavailable, $"Google Drive couldn't complete the request for {target}."),
            _ => error
        };
    }

    private static bool IsStoreError(Exception error) =>
        error is TokenResponseException or GoogleApiException or HttpRequestException;

    private static bool IsRateLimit(GoogleApiException error) =>
        error.Error?.Errors?.Any(e => e.Reason is "rateLimitExceeded" or "userRateLimitExceeded") == true;
}
