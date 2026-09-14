using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace BrowserTools;

public sealed class UnsafePathException : Exception
{
    public UnsafePathException(string message) : base(message) { }
}

public static partial class ScreenshotPaths
{
    [GeneratedRegex(@"^[A-Za-z0-9._][A-Za-z0-9._/-]*$")]
    private static partial Regex SafeRelativePath();

    private static int _filenameCounter;

    public static string GetDefaultDirectory()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "mcp-screenshots");

    public static string ResolveSafe(string baseDir, string relativeName)
    {
        if (string.IsNullOrEmpty(relativeName))
            throw new UnsafePathException("Screenshot name must be a non-empty string");
        if (relativeName.Contains('\0'))
            throw new UnsafePathException("Screenshot name must not contain null bytes");
        if (Path.IsPathRooted(relativeName)
            || (relativeName.Length >= 2 && char.IsAsciiLetter(relativeName[0]) && relativeName[1] == ':'))
            throw new UnsafePathException("Screenshot name must be relative");
        if (!SafeRelativePath().IsMatch(relativeName))
            throw new UnsafePathException($"Screenshot name contains unsupported characters: {relativeName}");

        var baseFull = Path.GetFullPath(baseDir);
        var resolved = Path.GetFullPath(Path.Combine(baseFull, relativeName.Replace('/', Path.DirectorySeparatorChar)));
        if (!string.Equals(resolved, baseFull, StringComparison.OrdinalIgnoreCase)
            && !resolved.StartsWith(baseFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new UnsafePathException("Screenshot name must stay inside the screenshot directory");

        return resolved;
    }

    public static string Filename(DateTimeOffset? now = null, string extension = "png")
    {
        var stamp = (now ?? DateTimeOffset.UtcNow).ToString("yyyy-MM-ddTHH-mm-ss.fffZ");
        var counter = (Interlocked.Increment(ref _filenameCounter) % 0x1000).ToString("x3");
        var random = Convert.ToHexString(RandomNumberGenerator.GetBytes(2))[..3].ToLowerInvariant();
        return $"screenshot-{stamp}-{random}{counter}.{extension}";
    }

    public static string WithExtension(string name, string extension)
    {
        var slash = Math.Max(name.LastIndexOf('/'), name.LastIndexOf('\\'));
        var dir = slash == -1 ? "" : name[..(slash + 1)];
        var baseName = slash == -1 ? name : name[(slash + 1)..];
        var dot = baseName.LastIndexOf('.');
        var stem = dot <= 0 ? baseName : baseName[..dot];
        return $"{dir}{stem}.{extension}";
    }
}
