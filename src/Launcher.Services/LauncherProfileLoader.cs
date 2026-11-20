using System.Text.Json;
using Launcher.Core;
using Launcher.Services.Providers;

namespace Launcher.Services;

public static class LauncherProfileLoader
{
    public static LauncherProfile Load(string[] args, string applicationDirectory, ProviderRegistry providers)
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

        var profileDirectory = Path.GetDirectoryName(profilePath)!;
        if (data.InstallId is { } installId && !PathSafety.IsPlainName(installId))
        {
            throw new InvalidDataException("The installId must be a plain folder name.");
        }

        if (data.Executable is { } executable && (string.IsNullOrWhiteSpace(executable) || Path.IsPathRooted(executable)))
        {
            throw new InvalidDataException("The executable must be a path relative to the build folder, such as App.exe.");
        }

        return new LauncherProfile(
            DisplayName: string.IsNullOrWhiteSpace(data.DisplayName) ? LauncherProfile.Default.DisplayName : data.DisplayName,
            BackgroundPath: ResolvePath(data.BackgroundImage, profileDirectory),
            UpdateSource: ParseUpdateSource(data, profileDirectory, providers),
            InstalledVersion: data.InstalledVersion is null ? null : LauncherJson.ParseVersion(data.InstalledVersion, "installedVersion"),
            InstallId: data.InstallId,
            Executable: data.Executable);
    }

    private static string? ResolvePath(string? path, string profileDirectory) =>
        string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path, profileDirectory);

    /// <summary>Reads a standalone <c>{ "type": ..., ... }</c> source file, such as a publish target.</summary>
    public static UpdateSourceConfig LoadSource(string path, ProviderRegistry providers)
    {
        var fullPath = Path.GetFullPath(path);
        using var stream = File.OpenRead(fullPath);
        using var document = JsonDocument.Parse(stream);
        return ParseSource(document.RootElement, Path.GetDirectoryName(fullPath)!, providers);
    }

    private static UpdateSourceConfig? ParseUpdateSource(ProfileData data, string profileDirectory, ProviderRegistry providers)
    {
        if (data.UpdateSource is { } source)
        {
            if (data.UpdateFeedUrl is not null)
            {
                throw new InvalidDataException("Use either updateSource or updateFeedUrl in the launcher profile, not both.");
            }

            return ParseSource(source, profileDirectory, providers);
        }

        if (data.UpdateFeedUrl is null)
        {
            return null;
        }

        // Older profiles name the manifest URL directly; treat them as an http-json source.
        var legacy = JsonSerializer.SerializeToElement(new { url = data.UpdateFeedUrl });
        return providers.Parse(HttpJsonSourceFactory.TypeName, legacy, profileDirectory);
    }

    private static UpdateSourceConfig ParseSource(JsonElement source, string baseDirectory, ProviderRegistry providers)
    {
        if (source.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("The update source must be a JSON object.");
        }

        var type = SettingsJson.RequireString(source, "type", "configured");
        return providers.Parse(type, source, baseDirectory);
    }

    private sealed record ProfileData(
        string? DisplayName,
        string? BackgroundImage,
        string? InstallId,
        string? Executable,
        string? UpdateFeedUrl,
        JsonElement? UpdateSource,
        string? InstalledVersion);
}
