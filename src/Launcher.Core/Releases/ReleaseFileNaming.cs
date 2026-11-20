using System.Diagnostics.CodeAnalysis;

namespace Launcher.Core.Releases;

/// <summary>The naming convention that ties a stored file to a version, such as <c>build-1.3.0.zip</c>.</summary>
public sealed class ReleaseFileNaming(string prefix = "build-", string extension = ".zip")
{
    public static ReleaseFileNaming Default { get; } = new();

    public string Format(Version version) => prefix + version + extension;

    public bool TryParse(string fileName, [NotNullWhen(true)] out Version? version)
    {
        version = null;
        if (fileName.Length <= prefix.Length + extension.Length
            || !fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || !fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var text = fileName[prefix.Length..^extension.Length];
        return text.All(c => char.IsAsciiDigit(c) || c == '.') && Version.TryParse(text, out version);
    }
}
