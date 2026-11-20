using System.IO;
using System.IO.Compression;
using System.Text;

namespace Launcher.Tests.Fakes;

internal static class Zips
{
    /// <summary>Builds a ZIP archive in memory from (entry name, text content) pairs.</summary>
    public static byte[] Create(params (string Name, string Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var stream = archive.CreateEntry(name).Open();
                stream.Write(Encoding.UTF8.GetBytes(content));
            }
        }

        return buffer.ToArray();
    }

    /// <summary>Writes a ZIP archive built from the entries to <paramref name="path"/>, creating its folder.</summary>
    public static void Write(string path, params (string Name, string Content)[] entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Create(entries));
    }
}
