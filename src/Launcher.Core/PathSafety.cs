namespace Launcher.Core;

/// <summary>Guards against paths and names that escape a folder or are not a single file name.</summary>
public static class PathSafety
{
    /// <summary>True only when <paramref name="relativePath"/>, combined with <paramref name="root"/>, lies inside <paramref name="root"/>.</summary>
    public static bool TryResolveInside(string root, string relativePath, out string fullPath)
    {
        var baseDirectory = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        fullPath = Path.GetFullPath(Path.Combine(baseDirectory, relativePath));
        return fullPath.StartsWith(baseDirectory, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when <paramref name="name"/> is a single, non-empty file or folder name with no separators or invalid characters.</summary>
    public static bool IsPlainName(string name) =>
        !string.IsNullOrWhiteSpace(name)
        && name is not ("." or "..")
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && Path.GetFileName(name) == name;
}
