namespace BrowserTools.Mcp;

public static class App
{
    public static int Run(HostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.ShowVersion)
        {
            Console.Out.WriteLine(Constants.ServerVersion);
            return 0;
        }

        if (options.ShowHelp)
        {
            Console.Out.Write(HelpText);
            return 0;
        }

        return RunAsync(options).GetAwaiter().GetResult();
    }

    private static async Task<int> RunAsync(HostOptions options)
    {
        if (options.Doctor)
            return await Doctor.RunAsync(options);

        await using var runtime = await Runtime.CreateAsync(options);
        Log.Info("main", $"Telemetry source: {runtime.Description}");
        if (runtime.DegradedReason is not null)
        {
            Log.Warn("main",
                "Running without a connector — tool calls will explain the problem rather than return data.");
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            await McpStdioServer.RunAsync(runtime.Client, cts.Token);
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error("main", $"Fatal error: {ex.Message}");
            return 1;
        }
    }

    public const string HelpText =
        """
        BrowserTools MCP

        Usage:
          browser-tools-mcp [options]

        Options:
          -v, --version            Print the version and exit
          -h, --help               Print this help and exit
              --doctor             Check the local setup and exit
              --port <n>           Port to listen on (default 3025)
              --host <addr>        Loopback address to bind (default 127.0.0.1)
              --screenshot-dir <p> Where screenshots are written
              --connect <url>      Attach to an existing connector
              --token <token>      Bearer token (required with --connect)
              --verbose            Print each captured entry as it arrives
              --no-redact          Do not scrub credentials from captured data

        Pair this process with the unchanged Chrome extension from browser-tools-mcp.
        Open DevTools (F12) on the page you want inspected.

        """;
}
