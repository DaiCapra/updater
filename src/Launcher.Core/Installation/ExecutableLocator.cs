namespace Launcher.Core.Installation;

/// <summary>Finds the executable in an installed build.</summary>
public static class ExecutableLocator
{
    public static string Find(string buildDirectory, string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (!PathSafety.TryResolveInside(buildDirectory, configured, out var path) || !File.Exists(path))
            {
                throw new InvalidDataException($"The installed build has no {configured}. Check the executable setting in the launcher profile.");
            }

            return path;
        }

        var candidates = Directory.GetFiles(Path.GetFullPath(buildDirectory), "*.exe")
            .Where(file => !Path.GetFileName(file).StartsWith("UnityCrashHandler", StringComparison.OrdinalIgnoreCase))
            .ToList();
        return candidates.Count == 1
            ? candidates[0]
            : throw new InvalidDataException(candidates.Count == 0
                ? "The installed build has no .exe file. Set executable in the launcher profile."
                : $"The installed build has several .exe files ({string.Join(", ", candidates.Select(Path.GetFileName))}). Set executable in the launcher profile.");
    }
}
