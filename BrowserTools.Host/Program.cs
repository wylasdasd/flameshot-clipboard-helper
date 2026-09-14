using BrowserTools.Mcp;

namespace BrowserTools.Host;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            return App.Run(Parse(args));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static HostOptions Parse(string[] args)
    {
        var showVersion = false;
        var showHelp = false;
        var doctor = false;
        int? port = null;
        string? host = null;
        string? screenshotDir = null;
        string? connectUrl = null;
        string? token = Environment.GetEnvironmentVariable("BROWSER_TOOLS_TOKEN");
        var redact = !string.Equals(
            Environment.GetEnvironmentVariable("BROWSER_TOOLS_REDACT"),
            "false",
            StringComparison.OrdinalIgnoreCase);
        var verbose = IsTruthy(Environment.GetEnvironmentVariable("BROWSER_TOOLS_VERBOSE"));

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            string Next()
            {
                if (i + 1 >= args.Length)
                    throw new InvalidOperationException($"Missing value for {arg}");
                return args[++i];
            }

            switch (arg)
            {
                case "-v":
                case "--version":
                    showVersion = true;
                    break;
                case "-h":
                case "--help":
                    showHelp = true;
                    break;
                case "--doctor":
                    doctor = true;
                    break;
                case "--port":
                    port = int.Parse(Next());
                    break;
                case "--host":
                    host = Next();
                    break;
                case "--screenshot-dir":
                    screenshotDir = Next();
                    break;
                case "--connect":
                    connectUrl = Next();
                    break;
                case "--token":
                    token = Next();
                    break;
                case "--verbose":
                    verbose = true;
                    break;
                case "--no-redact":
                    redact = false;
                    break;
                default:
                    throw new InvalidOperationException($"Unknown argument: {arg}");
            }
        }

        return new HostOptions
        {
            ShowVersion = showVersion,
            ShowHelp = showHelp,
            Doctor = doctor,
            Port = port,
            Host = host,
            ScreenshotDir = screenshotDir,
            ConnectUrl = connectUrl,
            Token = token,
            Redact = redact,
            Verbose = verbose
        };
    }

    private static bool IsTruthy(string? value)
        => value is "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
}
