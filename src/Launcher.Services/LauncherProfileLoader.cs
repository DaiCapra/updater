using System.Text.Json;
using Launcher.Core;

namespace Launcher.Services;

public static class LauncherProfileLoader
{
    public static LauncherProfile Load(string[] args, string applicationDirectory)
    {
        var profilePath = Path.Combine(applicationDirectory, "launcher.json");
        if (args is ["--profile", var explicitPath] && !string.IsNullOrWhiteSpace(explicitPath))
        {
            profilePath = Path.GetFullPath(explicitPath);
        }
        else if (args.Length != 0)
        {
            throw new InvalidDataException("Usage: Launcher.exe [--profile path-to-launcher.json]");
        }
        else if (!Path.Exists(profilePath))
        {
            return LauncherProfile.Default;
        }

        using var stream = File.OpenRead(profilePath);
        var data = JsonSerializer.Deserialize<ProfileData>(stream, LauncherJson.Options)
            ?? throw new InvalidDataException("The launcher profile must be a JSON object.");

        if ((data.UpdateFeedUrl is null) != (data.InstalledVersion is null))
        {
            throw new InvalidDataException("Set both updateFeedUrl and installedVersion in the launcher profile.");
        }

        return new LauncherProfile(
            DisplayName: string.IsNullOrWhiteSpace(data.DisplayName) ? LauncherProfile.Default.DisplayName : data.DisplayName,
            BackgroundPath: string.IsNullOrWhiteSpace(data.BackgroundImage)
                ? null
                : Path.GetFullPath(data.BackgroundImage, Path.GetDirectoryName(profilePath)!),
            UpdateFeedUrl: data.UpdateFeedUrl is null ? null : LauncherJson.ParseHttpsUrl(data.UpdateFeedUrl, "updateFeedUrl"),
            InstalledVersion: data.InstalledVersion is null ? null : LauncherJson.ParseVersion(data.InstalledVersion, "installedVersion"));
    }

    private sealed record ProfileData(
        string? DisplayName, string? BackgroundImage, string? UpdateFeedUrl, string? InstalledVersion);
}
