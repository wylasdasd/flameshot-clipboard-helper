namespace BrowserTools.Mcp;

public sealed class Runtime : IAsyncDisposable
{
    public required IConnectorClient Client { get; init; }
    public Connector? Connector { get; init; }
    public required string Description { get; init; }
    public string? DegradedReason { get; init; }
    public Action? OnClose { get; init; }

    public static async Task<Runtime> CreateAsync(HostOptions options)
    {
        if (!string.IsNullOrEmpty(options.ConnectUrl))
        {
            var token = options.Token ?? SessionFile.Read()?.Token ?? "";
            if (string.IsNullOrEmpty(token))
                throw new InvalidOperationException("--connect requires --token (or a readable session file)");
            return new Runtime
            {
                Client = new HttpConnectorClient(options.ConnectUrl, token),
                Description = $"attached to the connector at {options.ConnectUrl}"
            };
        }

        var existing = SessionFile.Read();
        if (existing is not null && await ProbeAsync($"http://127.0.0.1:{existing.Port}"))
        {
            Log.Info("runtime", $"Attaching to the connector already running on port {existing.Port}");
            return new Runtime
            {
                Client = new HttpConnectorClient($"http://127.0.0.1:{existing.Port}", existing.Token),
                Description = $"attached to the shared connector on port {existing.Port}"
            };
        }

        try
        {
            var connector = await Connector.StartAsync(new ConnectorConfig
            {
                Port = options.Port ?? Constants.DefaultPort,
                Host = options.Host ?? Constants.LoopbackHost,
                ScreenshotDir = options.ScreenshotDir,
                Token = options.Token,
                Redact = options.Redact,
                Verbose = options.Verbose
            });

            SessionFile.Write(new SessionInfo
            {
                Port = connector.Port,
                Token = connector.Token,
                Pid = Environment.ProcessId,
                StartedAt = DateTimeOffset.UtcNow.ToString("o"),
                Version = Constants.ServerVersion
            });

            return new Runtime
            {
                Client = new InProcessConnectorClient(connector),
                Connector = connector,
                Description = $"embedded connector on port {connector.Port}",
                OnClose = SessionFile.Clear
            };
        }
        catch (Exception ex)
        {
            Log.Error("runtime", $"Could not start the local connector: {ex.Message}");
            return new Runtime
            {
                Client = new UnavailableConnectorClient(ex.Message),
                Description = "unavailable",
                DegradedReason = ex.Message
            };
        }
    }

    public async ValueTask DisposeAsync()
    {
        OnClose?.Invoke();
        if (Client is IDisposable disposable)
            disposable.Dispose();
        if (Connector is not null)
            await Connector.DisposeAsync();
    }

    public static async Task<bool> ProbeAsync(string baseUrl, int timeoutMs = 400)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(timeoutMs) };
            using var response = await http.GetAsync($"{baseUrl.TrimEnd('/')}{Constants.IdentityPath}");
            if (!response.IsSuccessStatusCode)
                return false;
            var text = await response.Content.ReadAsStringAsync();
            var json = JsonUtil.Parse(text);
            return JsonUtil.GetString(json, "signature") == Constants.ServerSignature;
        }
        catch
        {
            return false;
        }
    }
}
