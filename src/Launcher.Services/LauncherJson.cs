using System.Text.Json;

namespace Launcher.Services;

internal static class LauncherJson
{
    internal static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    internal static Uri ParseHttpsUrl(string? value, string field)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || uri.Host.Length == 0 || uri.UserInfo.Length != 0)
        {
            throw new InvalidDataException($"The {field} field must be an HTTPS URL without credentials.");
        }

        return uri;
    }

    internal static Version ParseVersion(string? value, string field)
    {
        if (!Version.TryParse(value, out var version))
        {
            throw new InvalidDataException($"The {field} field must be a version with two to four numeric components.");
        }

        return version;
    }
}
