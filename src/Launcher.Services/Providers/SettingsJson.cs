using System.Text.Json;

namespace Launcher.Services.Providers;

/// <summary>Helpers providers use to read their fields from an <c>updateSource</c> object.</summary>
public static class SettingsJson
{
    public static string RequireString(JsonElement json, string field, string type)
    {
        if (json.ValueKind != JsonValueKind.Object
            || !json.TryGetProperty(field, out var value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new InvalidDataException($"The {type} update source needs a {field} field.");
        }

        return value.GetString()!;
    }

    public static string RequirePath(JsonElement json, string field, string type, string baseDirectory) =>
        Path.GetFullPath(RequireString(json, field, type), baseDirectory);
}
