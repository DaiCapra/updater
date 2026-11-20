using Launcher.Core;
using Launcher.Core.Releases;

namespace Launcher;

public sealed class MainWindowViewModel(
    LauncherProfile profile, UpdateChecker? updateChecker = null, IReleaseDownloader? releaseDownloader = null)
{
    public string Title => profile.DisplayName;
    public string? BackgroundPath => profile.BackgroundPath;
    public bool CanCheckForUpdates => updateChecker is not null && profile.InstalledVersion is not null;

    internal Task<UpdateRelease?> CheckForUpdatesAsync()
    {
        if (updateChecker is null || profile.InstalledVersion is not { } installedVersion)
        {
            throw new InvalidOperationException("No update source is configured.");
        }

        return updateChecker.CheckAsync(installedVersion);
    }

    internal bool CanDownload(UpdateRelease release) => releaseDownloader is not null && release.File is not null;

    internal Task<string> DownloadAsync(UpdateRelease release, IProgress<double> progress, CancellationToken cancellationToken) =>
        releaseDownloader is not null && release.File is not null
            ? releaseDownloader.DownloadAsync(release, progress, cancellationToken)
            : throw new InvalidOperationException("This release can't be downloaded in the launcher.");
}
