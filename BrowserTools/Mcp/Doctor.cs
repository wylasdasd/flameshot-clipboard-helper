using System.Text.Json.Nodes;

namespace BrowserTools.Mcp;

public static class Doctor
{
    public static async Task<int> RunAsync(HostOptions options)
    {
        var problems = 0;
        void Line(string label, string value) => Console.Error.WriteLine($"{label.PadRight(22)} {value}");

        Console.Error.WriteLine("BrowserTools MCP — setup check");
        Console.Error.WriteLine();
        Line("Version", Constants.ServerVersion);
        Line("Runtime", $".NET {Environment.Version} (ok)");
        Line("Platform", $"{Environment.OSVersion.Platform} {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}");
        Line("Screenshot directory", options.ScreenshotDir ?? ScreenshotPaths.GetDefaultDirectory());

        var session = SessionFile.Read();
        Line("Session file", session is null ? "none" : $"{SessionFile.FilePath()} (port {session.Port}, pid {session.Pid})");

        await using var runtime = await Runtime.CreateAsync(options);
        Line("Connector", runtime.DegradedReason is null ? runtime.Description : $"failed — {runtime.DegradedReason}");
        if (runtime.DegradedReason is not null)
            problems += 1;

        var attached = runtime.Connector is null && runtime.DegradedReason is null;
        if (attached)
            Console.Error.WriteLine("  · This check attached to a connector that was already running (shared HTTP).");

        try
        {
            var connected = false;
            JsonObject? status = null;
            for (var attempt = 1; attempt <= 5; attempt++)
            {
                status = await runtime.Client.StatusAsync();
                connected = status["extensionConnected"]?.GetValue<bool>() ?? false;
                if (connected)
                    break;
                if (attempt < 5)
                    await Task.Delay(400);
            }

            Line("Chrome extension", connected ? "connected" : "not connected");
            if (!connected)
            {
                problems += 1;
                Console.Error.WriteLine("  ! Open Chrome DevTools (F12) on the page you want to inspect.");
                Console.Error.WriteLine("    Capture starts as soon as DevTools is open — the BrowserTools panel is optional.");
                Console.Error.WriteLine("    Load the unchanged chrome-extension directory at chrome://extensions.");
            }
            else
            {
                var counts = status?["counts"]?.AsObject();
                Line("Captured entries", $"{counts?["console"]} console, {counts?["network"]} network");
            }
        }
        catch (Exception ex)
        {
            problems += 1;
            Line("Chrome extension", "unknown");
            Console.Error.WriteLine($"  ! Could not query the connector: {ex.Message}");
        }

        try
        {
            var logs = await runtime.Client.ConsoleAsync(new ConsoleQuery { Limit = 1 });
            Line("HTTP console read", $"ok (total {logs.Total})");
        }
        catch (Exception ex)
        {
            problems += 1;
            Line("HTTP console read", "failed");
            Console.Error.WriteLine($"  ! {ex.Message}");
        }

        Line("Audit browser", "not bundled (Lighthouse is out of process in this build)");
        Console.Error.WriteLine();
        Console.Error.WriteLine(problems == 0
            ? "All checks passed."
            : $"{problems} issue(s) found. The MCP server can still start; tool calls will explain what is missing.");
        return problems == 0 ? 0 : 1;
    }
}
