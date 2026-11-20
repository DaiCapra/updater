namespace Launcher.Core.Storage;

/// <summary>A file in a release store. <see cref="Key"/> is the store's own identifier (a Drive file ID, an S3 key, ...).</summary>
public sealed record StoredFile(string Key, string Name, long Size, FileChecksum? Checksum);

/// <summary>A checksum reported by the store. <see cref="Value"/> is hex; comparisons ignore case.</summary>
public sealed record FileChecksum(string Algorithm, string Value);
