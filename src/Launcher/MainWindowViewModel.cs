using Launcher.Core;

namespace Launcher;

public sealed class MainWindowViewModel(LauncherProfile profile, UpdateChecker? updateChecker = null)
{
    public string Title => profile.DisplayName;
    public string? BackgroundPath => profile.BackgroundPath;
    public bool CanCheckForUpdates => updateChecker is not null && profile.InstalledVersion is not null;

    internal Task<UpdateRelease?> CheckForUpdatesAsync()
    {
        if (updateChecker is null || profile.InstalledVersion is not { } installedVersion)
        {
            throw new InvalidOperationException("No update feed is configured.");
        }

        return updateChecker.CheckAsync(installedVersion);
    }
}
