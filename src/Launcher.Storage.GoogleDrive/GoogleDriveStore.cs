using Google.Apis.Download;
using Google.Apis.Drive.v3;
using Google.Apis.Upload;
using Launcher.Core.Storage;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace Launcher.Storage.GoogleDrive;

/// <summary>
/// Lists, downloads and uploads the files directly inside one Drive folder. The provider hands out
/// read-only or read-write instances depending on the scope it requested.
/// </summary>
internal sealed class GoogleDriveStore(DriveService service, string folderId, string account)
    : IReleaseStoreReader, IReleaseStoreWriter, IDisposable
{
    private const string FileFields = "id, name, size, md5Checksum";
    private const string FolderMimeType = "application/vnd.google-apps.folder";
    private const int ChunkSize = 1024 * 1024;

    private readonly GoogleDriveErrors errors = new(folderId, account);

    public async Task<IReadOnlyList<StoredFile>> ListAsync(CancellationToken cancellationToken)
    {
        await errors.Guard(EnsureFolderAsync(cancellationToken)).ConfigureAwait(false);

        var files = new List<StoredFile>();
        string? pageToken = null;
        do
        {
            var request = service.Files.List();
            request.Q = $"'{folderId}' in parents and trashed = false and mimeType != '{FolderMimeType}'";
            request.Fields = $"nextPageToken, files({FileFields})";
            request.PageSize = 1000;
            request.PageToken = pageToken;
            request.SupportsAllDrives = true;
            request.IncludeItemsFromAllDrives = true;

            var page = await errors.Guard(request.ExecuteAsync(cancellationToken)).ConfigureAwait(false);
            files.AddRange((page.Files ?? []).Select(ToStoredFile));
            pageToken = page.NextPageToken;
        }
        while (!string.IsNullOrEmpty(pageToken));

        return files;
    }

    public async Task DownloadAsync(StoredFile file, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        var request = GetFile(file.Key);
        request.MediaDownloader.ChunkSize = ChunkSize;
        request.MediaDownloader.ProgressChanged += update =>
        {
            if (update.Status is DownloadStatus.Downloading or DownloadStatus.Completed)
            {
                progress?.Report(update.BytesDownloaded);
            }
        };

        var result = await request.DownloadAsync(destination, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (result.Status != DownloadStatus.Completed)
        {
            throw errors.Translate(result.Exception, file.Name);
        }
    }

    public async Task<StoredFile> UploadAsync(string name, Stream content, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        var metadata = new DriveFile { Name = name, Parents = [folderId] };
        var request = service.Files.Create(metadata, content, "application/octet-stream");
        request.Fields = FileFields;
        request.SupportsAllDrives = true;
        request.ChunkSize = ResumableUpload.MinimumChunkSize * 4;
        request.ProgressChanged += update =>
        {
            if (update.Status is UploadStatus.Uploading or UploadStatus.Completed)
            {
                progress?.Report(update.BytesSent);
            }
        };

        var result = await request.UploadAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (result.Status != UploadStatus.Completed || request.ResponseBody is null)
        {
            throw errors.Translate(result.Exception, name);
        }

        return ToStoredFile(request.ResponseBody);
    }

    public async Task DeleteAsync(StoredFile file, CancellationToken cancellationToken)
    {
        var request = service.Files.Delete(file.Key);
        request.SupportsAllDrives = true;
        await errors.Guard(request.ExecuteAsync(cancellationToken), file.Name).ConfigureAwait(false);
    }

    public void Dispose() => service.Dispose();

    private FilesResource.GetRequest GetFile(string fileId)
    {
        var request = service.Files.Get(fileId);
        request.SupportsAllDrives = true;
        return request;
    }

    /// <summary>Listing a folder the account can't see returns nothing, so check the folder itself first.</summary>
    private async Task EnsureFolderAsync(CancellationToken cancellationToken)
    {
        var request = GetFile(folderId);
        request.Fields = "id, mimeType";
        var folder = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        if (folder.MimeType != FolderMimeType)
        {
            throw new ReleaseStoreException(ReleaseStoreErrorKind.NotFound,
                $"The Google Drive ID {folderId} belongs to a file, not a folder. Use the release folder's ID.");
        }
    }

    private static StoredFile ToStoredFile(DriveFile file) => new(
        file.Id,
        file.Name,
        file.Size ?? 0,
        string.IsNullOrEmpty(file.Md5Checksum) ? null : new FileChecksum("md5", file.Md5Checksum));
}
