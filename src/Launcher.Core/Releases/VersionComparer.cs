namespace Launcher.Core.Releases;

/// <summary>Compares versions treating missing components as zero, so 1.2 equals 1.2.0.</summary>
public sealed class VersionComparer : IComparer<Version>, IEqualityComparer<Version>
{
    public static VersionComparer Instance { get; } = new();

    public static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));

    public int Compare(Version? x, Version? y) =>
        x is null || y is null ? Comparer<Version?>.Default.Compare(x, y) : Normalize(x).CompareTo(Normalize(y));

    public bool Equals(Version? x, Version? y) => Compare(x, y) == 0;

    public int GetHashCode(Version version) => Normalize(version).GetHashCode();
}
