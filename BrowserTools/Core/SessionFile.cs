using System.Security.Cryptography;
using System.Text.Json;

namespace BrowserTools;

public static class SessionFile
{
    public static string DirectoryPath()
        => Environment.GetEnvironmentVariable("BROWSER_TOOLS_STATE_DIR")
           ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Constants.SessionDirectoryName);

    public static string FilePath() => Path.Combine(DirectoryPath(), Constants.SessionFileName);

    public static string GenerateToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    public static bool TokensMatch(string a, string b)
    {
        var ba = System.Text.Encoding.UTF8.GetBytes(a);
        var bb = System.Text.Encoding.UTF8.GetBytes(b);
        if (ba.Length != bb.Length)
            return false;
        return CryptographicOperations.FixedTimeEquals(ba, bb);
    }

    public static void Write(SessionInfo info)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath());
            var json = JsonSerializer.Serialize(info, BrowserToolsJsonContext.Default.SessionInfo);
            System.IO.File.WriteAllText(FilePath(), json);
        }
        catch (Exception ex)
        {
            Log.Warn("session", $"Could not write session file: {ex.Message}");
        }
    }

    public static SessionInfo? Read()
    {
        try
        {
            var parsed = JsonSerializer.Deserialize(System.IO.File.ReadAllText(FilePath()), BrowserToolsJsonContext.Default.SessionInfo);
            if (parsed is null || parsed.Port <= 0 || string.IsNullOrEmpty(parsed.Token))
                return null;
            return parsed;
        }
        catch
        {
            return null;
        }
    }

    public static void Clear()
    {
        try
        {
            if (System.IO.File.Exists(FilePath()))
                System.IO.File.Delete(FilePath());
        }
        catch
        {
            // nothing to clean up
        }
    }
}
