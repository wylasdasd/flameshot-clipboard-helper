using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BrowserTools.Mcp;

public sealed class ConnectorConfig
{
    public string Host { get; init; } = Constants.LoopbackHost;
    public int Port { get; init; } = Constants.DefaultPort;
    public string? Token { get; init; }
    public string? ScreenshotDir { get; init; }
    public bool Redact { get; init; } = true;
    public bool Verbose { get; init; }
    public int HeartbeatIntervalMs { get; init; } = 15_000;
    public int RequestTimeoutMs { get; init; } = 10_000;
}

public sealed class TabView
{
    public string TabId { get; init; } = "";
    public string Url { get; init; } = "";
    public bool IsCurrent { get; init; }
    public int ConsoleCount { get; init; }
    public int NetworkCount { get; init; }
}

public sealed class Connector : IAsyncDisposable
{
    private readonly ConnectorConfig _config;
    private readonly TelemetryStore _store;
    private readonly string _token;
    private readonly string _screenshotDir;
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<string, ExtensionConnection> _connections = new();
    private readonly ConcurrentDictionary<string, PendingRequest> _pending = new();
    private readonly Dictionary<string, TabRecord> _tabs = new(StringComparer.Ordinal);
    private readonly HashSet<string> _seenTabIds = new(StringComparer.Ordinal);
    private readonly object _tabLock = new();
    private string? _currentTabId;
    private Task? _acceptLoop;
    private Task? _heartbeat;

    public Connector(ConnectorConfig config, HttpListener listener, int port, string token, string screenshotDir)
    {
        _config = config;
        _listener = listener;
        Port = port;
        _token = token;
        _screenshotDir = screenshotDir;
        Host = config.Host;
        _store = new TelemetryStore(config.Redact);
        Token = token;
    }

    public int Port { get; }
    public string Host { get; }
    public string Token { get; }
    public string ScreenshotDir => _screenshotDir;
    public TelemetryStore Store => _store;

    public static async Task<Connector> StartAsync(ConnectorConfig config, CancellationToken cancellationToken = default)
    {
        if (!Security.IsLoopbackHost(config.Host))
        {
            throw new InvalidOperationException(
                $"Refusing to bind {config.Host}: the connector accepts loopback addresses only.");
        }

        var token = string.IsNullOrEmpty(config.Token) ? SessionFile.GenerateToken() : config.Token;
        var screenshotDir = Path.GetFullPath(config.ScreenshotDir ?? ScreenshotPaths.GetDefaultDirectory());
        Directory.CreateDirectory(screenshotDir);

        var startPort = config.Port;
        HttpListener? listener = null;
        var port = startPort;
        Exception? last = null;
        for (var attempt = 0; attempt < 11; attempt++)
        {
            port = startPort == 0 ? 0 : startPort + attempt;
            var candidate = new HttpListener();
            candidate.Prefixes.Add($"http://{config.Host}:{port}/");
            try
            {
                candidate.Start();
                if (port == 0)
                {
                    var prefix = candidate.Prefixes.First();
                    port = new Uri(prefix).Port;
                }

                listener = candidate;
                break;
            }
            catch (Exception ex)
            {
                candidate.Close();
                last = ex;
                if (startPort == 0)
                    break;
            }
        }

        if (listener is null)
            throw last ?? new InvalidOperationException("Could not bind a loopback port.");

        var connector = new Connector(config, listener, port, token, screenshotDir);
        connector._acceptLoop = Task.Run(() => connector.AcceptLoopAsync(), CancellationToken.None);
        connector._heartbeat = Task.Run(() => connector.HeartbeatLoopAsync(), CancellationToken.None);
        Log.Info("connector", $"Connector listening on http://{config.Host}:{port}");
        await Task.CompletedTask.WaitAsync(cancellationToken);
        return connector;
    }

    public bool HasExtension() => !_connections.IsEmpty;

    public string? GetCurrentTabId()
    {
        lock (_tabLock)
            return _currentTabId;
    }

    public List<TabView> ListTabs()
    {
        lock (_tabLock)
        {
            return _tabs.Values.Select(record => new TabView
            {
                TabId = record.TabId,
                Url = record.Url,
                IsCurrent = record.TabId == _currentTabId,
                ConsoleCount = _store.QueryConsole(new ConsoleQuery { TabId = record.TabId }).Total,
                NetworkCount = _store.QueryNetwork(new NetworkQuery { TabId = record.TabId }).Total
            }).ToList();
        }
    }

    public TabScopedResult<ConsoleEntry> QueryConsole(ConsoleQuery query)
    {
        var scoped = Scope(query.AllTabs, query.TabId);
        var result = _store.QueryConsole(scoped.TabId is null
            ? new ConsoleQuery
            {
                ErrorsOnly = query.ErrorsOnly,
                Keywords = query.Keywords,
                Limit = query.Limit,
                Offset = query.Offset
            }
            : new ConsoleQuery
            {
                ErrorsOnly = query.ErrorsOnly,
                Keywords = query.Keywords,
                TabId = scoped.TabId,
                Limit = query.Limit,
                Offset = query.Offset
            });
        return TabScopedResult<ConsoleEntry>.From(result, scoped);
    }

    public TabScopedResult<NetworkEntry> QueryNetwork(NetworkQuery query)
    {
        var scoped = Scope(query.AllTabs, query.TabId);
        var result = _store.QueryNetwork(scoped.TabId is null
            ? new NetworkQuery
            {
                ErrorsOnly = query.ErrorsOnly,
                UrlKeywords = query.UrlKeywords,
                BodyKeywords = query.BodyKeywords,
                Limit = query.Limit,
                Offset = query.Offset
            }
            : new NetworkQuery
            {
                ErrorsOnly = query.ErrorsOnly,
                UrlKeywords = query.UrlKeywords,
                BodyKeywords = query.BodyKeywords,
                TabId = scoped.TabId,
                Limit = query.Limit,
                Offset = query.Offset
            });
        return TabScopedResult<NetworkEntry>.From(result, scoped);
    }

    public JsonElement? GetSelectedElement(string? tabId = null)
    {
        var resolved = ResolveTabId(tabId);
        return resolved is null ? _store.GetSelectedElement() : _store.GetSelectedElement(resolved);
    }

    public ExportBundle<ConsoleEntry> ExportConsole(string? tabId, bool allTabs)
    {
        var scoped = Scope(allTabs, tabId);
        return new ExportBundle<ConsoleEntry>(scoped.TabId, scoped.Url, _store.AllConsole(scoped.TabId));
    }

    public ExportBundle<NetworkEntry> ExportNetwork(string? tabId, bool allTabs)
    {
        var scoped = Scope(allTabs, tabId);
        return new ExportBundle<NetworkEntry>(scoped.TabId, scoped.Url, _store.AllNetwork(scoped.TabId));
    }

    public void Wipe(string? tabId) => _store.Wipe(tabId);

    public async Task<ScreenshotCapture> CaptureScreenshotAsync(string? name, string? tabId, CancellationToken cancellationToken)
    {
        var requestedName = name ?? ScreenshotPaths.Filename();
        ScreenshotPaths.ResolveSafe(_screenshotDir, requestedName);
        var connection = ConnectionForTab(tabId);
        var maxBytes = _store.Settings.ScreenshotMaxBytes;
        var response = await RequestFromExtensionAsync(connection, "capture-screenshot", new JsonObject
        {
            ["name"] = requestedName,
            ["maxBytes"] = maxBytes
        }, cancellationToken);

        var data = JsonUtil.GetString(response, "data");
        var parsed = ImageData.ParseDataUrl(data)
            ?? throw new ExtensionRequestException("The extension returned an empty or unsupported screenshot payload");
        var finalName = ScreenshotPaths.WithExtension(requestedName, ImageData.ExtensionForMime(parsed.MimeType));
        var destination = ScreenshotPaths.ResolveSafe(_screenshotDir, finalName);
        var bytes = ImageData.ApproximateBytes(parsed.Base64);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await File.WriteAllBytesAsync(destination, Convert.FromBase64String(parsed.Base64), cancellationToken);
        if (bytes > maxBytes)
            Log.Warn("connector", $"Screenshot is {bytes} bytes, over the {maxBytes} budget; saved to {destination}");

        return new ScreenshotCapture(
            destination,
            $"data:{parsed.MimeType};base64,{parsed.Base64}",
            finalName,
            parsed.MimeType,
            bytes,
            bytes <= maxBytes,
            connection.TabId,
            connection.TabId is not null ? TabUrl(connection.TabId) : _store.GetCurrentPage().Url);
    }

    public Task RefreshTabAsync(string? tabId, CancellationToken cancellationToken)
        => RequestFromExtensionAsync(ConnectionForTab(tabId), "refresh-tab", new JsonObject(), cancellationToken);

    public async Task<JsonElement> ReadStorageAsync(IReadOnlyList<string> kinds, string? tabId, CancellationToken cancellationToken)
    {
        var arr = new JsonArray();
        foreach (var kind in kinds)
            JsonUtil.Push(arr, kind);
        var response = await RequestFromExtensionAsync(ConnectionForTab(tabId), "get-storage", new JsonObject { ["kinds"] = arr }, cancellationToken);
        return JsonUtil.TryGet(response, "storage", out var storage) ? storage.Clone() : JsonUtil.Parse("{}");
    }

    public async Task<PageScriptResult> RunPageScriptAsync(string script, string? tabId, int? timeoutMs, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(script))
            throw new ExtensionRequestException("script is required");
        if (script.Length > 100_000)
            throw new ExtensionRequestException("script is too large");

        var timeout = timeoutMs is null or 0
            ? _config.RequestTimeoutMs
            : Math.Min(60_000, Math.Max(1_000, timeoutMs.Value));
        var connection = ConnectionForTab(tabId);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);
        var response = await RequestFromExtensionAsync(connection, "run-script", new JsonObject { ["script"] = script }, linked.Token);
        var raw = JsonUtil.TryGet(response, "result", out var resultEl) ? resultEl : JsonUtil.Null;
        var redacted = Redact.Value(raw, _config.Redact);
        var limited = Truncate.StringsInData(redacted, _store.Settings.StringSizeLimit);
        var scoped = Scope(false, tabId);
        return new PageScriptResult(
            limited,
            JsonUtil.GetString(response, "resultType") is { Length: > 0 } t ? t : raw.ValueKind.ToString().ToLowerInvariant(),
            JsonUtil.TryGet(response, "awaited", out var awaited) && awaited.ValueKind == JsonValueKind.True,
            JsonUtil.JsonSize(limited) < JsonUtil.JsonSize(redacted),
            scoped.TabId,
            scoped.Url,
            scoped.OtherTabs);
    }

    public async Task<InteractResult> InteractAsync(InteractRequest request, CancellationToken cancellationToken)
    {
        if (request.Action is not ("click" or "type" or "press" or "hover" or "scroll"))
            throw new ExtensionRequestException($"Unknown action: {request.Action}");
        if (request.Action == "type" && request.Text is null)
            throw new ExtensionRequestException("type requires text");
        if (request.Action == "press" && string.IsNullOrEmpty(request.Key))
            throw new ExtensionRequestException("press requires key");

        var payload = new JsonObject { ["action"] = request.Action };
        if (request.Selector is not null) payload["selector"] = request.Selector;
        if (request.Text is not null) payload["text"] = request.Text;
        if (request.Key is not null) payload["key"] = request.Key;
        if (request.X is not null) payload["x"] = request.X;
        if (request.Y is not null) payload["y"] = request.Y;
        if (request.DeltaX is not null) payload["deltaX"] = request.DeltaX;
        if (request.DeltaY is not null) payload["deltaY"] = request.DeltaY;

        var response = await RequestFromExtensionAsync(ConnectionForTab(request.TabId), "interact", payload, cancellationToken);
        var scoped = Scope(false, request.TabId);
        return new InteractResult(
            request.Action,
            (int)JsonUtil.GetNumber(response, "matched"),
            JsonUtil.TryGet(response, "x", out var x) && x.ValueKind == JsonValueKind.Number ? x.GetDouble() : null,
            JsonUtil.TryGet(response, "y", out var y) && y.ValueKind == JsonValueKind.Number ? y.GetDouble() : null,
            JsonUtil.GetString(response, "tagName") is { Length: > 0 } tag ? tag : null,
            scoped.TabId,
            scoped.Url,
            scoped.OtherTabs);
    }

    public Task<Artifact> ReadArtifactAsync(string kind, string name)
    {
        if (kind is not ("screenshot" or "audit"))
            throw new InvalidOperationException($"Unknown artifact: {kind}");
        var baseDir = kind == "screenshot" ? _screenshotDir : Path.Combine(_screenshotDir, "audits");
        var resolved = ScreenshotPaths.ResolveSafe(baseDir, name);
        var bytes = File.ReadAllBytes(resolved);
        if (kind == "audit")
            return Task.FromResult(new Artifact("application/json", Encoding.UTF8.GetString(bytes), null));
        var mime = resolved.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ? "image/jpeg" : "image/png";
        return Task.FromResult(new Artifact(mime, null, Convert.ToBase64String(bytes)));
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        foreach (var pending in _pending.Values)
            pending.Tcs.TrySetException(new Exception("Connector shutting down"));
        _pending.Clear();
        foreach (var connection in _connections.Values)
        {
            try { connection.Socket.Abort(); } catch { /* already gone */ }
        }

        _connections.Clear();
        try { _listener.Stop(); } catch { /* closing */ }
        _listener.Close();
        if (_acceptLoop is not null)
            try { await _acceptLoop; } catch { /* cancelled */ }
        if (_heartbeat is not null)
            try { await _heartbeat; } catch { /* cancelled */ }
        _cts.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().WaitAsync(_cts.Token);
            }
            catch (Exception) when (_cts.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Log.Warn("connector", $"HTTP accept error: {ex.Message}");
                continue;
            }

            _ = Task.Run(() => HandleContextAsync(context));
        }
    }

    private async Task HandleContextAsync(HttpListenerContext context)
    {
        try
        {
            var request = context.Request;
            var host = request.Headers["Host"];
            if (!Security.IsAllowedHost(host))
            {
                await WriteErrorAsync(context.Response, 403, "Requests must address this server as localhost", "FORBIDDEN_HOST");
                return;
            }

            var origin = request.Headers["Origin"];
            if (!Security.IsAllowedHttpOrigin(origin))
            {
                await WriteErrorAsync(context.Response, 403, "Cross-origin requests are not accepted", "FORBIDDEN_ORIGIN");
                return;
            }

            var path = request.Url?.AbsolutePath ?? "/";
            if (request.IsWebSocketRequest)
            {
                await HandleWebSocketAsync(context, path, origin);
                return;
            }

            await HandleHttpAsync(context, path);
        }
        catch (Exception ex)
        {
            Log.Warn("connector", $"Request error: {ex.Message}");
            try { context.Response.StatusCode = 500; context.Response.Close(); } catch { /* gone */ }
        }
    }

    private async Task HandleWebSocketAsync(HttpListenerContext context, string path, string? origin)
    {
        if (!string.Equals(path, Constants.ExtensionWebSocketPath, StringComparison.Ordinal))
        {
            context.Response.StatusCode = 404;
            context.Response.Close();
            return;
        }

        if (!string.IsNullOrEmpty(origin))
        {
            if (!Security.IsExtensionOrigin(origin))
            {
                Log.Warn("connector", $"Rejected websocket upgrade from origin {origin}");
                context.Response.StatusCode = 403;
                context.Response.Close();
                return;
            }
        }
        else
        {
            var presented = context.Request.QueryString["token"] ?? "";
            if (string.IsNullOrEmpty(presented) || !SessionFile.TokensMatch(presented, _token))
            {
                context.Response.StatusCode = 401;
                context.Response.Close();
                return;
            }
        }

        var wsContext = await context.AcceptWebSocketAsync(subProtocol: null);
        var connection = new ExtensionConnection
        {
            Id = Guid.NewGuid().ToString("N"),
            Context = wsContext,
            LastSeen = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        _connections[connection.Id] = connection;
        Log.Info("connector", $"Extension connected ({_connections.Count} active)");
        await SendJsonAsync(connection, WelcomeMessage());
        await ReceiveLoopAsync(connection);
    }

    private async Task ReceiveLoopAsync(ExtensionConnection connection)
    {
        var buffer = new byte[64 * 1024];
        using var message = new MemoryStream();
        try
        {
            while (connection.Socket.State == WebSocketState.Open && !_cts.IsCancellationRequested)
            {
                var result = await connection.Socket.ReceiveAsync(buffer, _cts.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;
                message.Write(buffer, 0, result.Count);
                if (message.Length > 10_000_000)
                {
                    Log.Warn("connector", "Discarded oversized websocket frame");
                    break;
                }

                if (!result.EndOfMessage)
                    continue;

                var json = Encoding.UTF8.GetString(message.ToArray());
                message.SetLength(0);
                try
                {
                    HandleExtensionMessage(connection, JsonUtil.Parse(json));
                }
                catch (Exception ex)
                {
                    Log.Warn("connector", $"Error handling extension message: {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (!_cts.IsCancellationRequested)
        {
            Log.Warn("connector", $"Extension socket error: {ex.Message}");
        }
        finally
        {
            DropConnection(connection.Id);
        }
    }

    private void HandleExtensionMessage(ExtensionConnection connection, JsonElement message)
    {
        connection.LastSeen = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var type = JsonUtil.GetString(message, "type");
        switch (type)
        {
            case "hello":
                if (JsonUtil.TabIdString(message, "tabId") is { } tabId)
                    BindTab(connection, tabId);
                break;
            case "pong":
                connection.AwaitingPong = false;
                connection.MissedPings = 0;
                break;
            case "console":
                foreach (var entry in AsEntries(message))
                {
                    var added = _store.AddConsole(entry, connection.TabId);
                    ReportCapture("console", added, connection.TabId);
                }
                break;
            case "network":
                foreach (var entry in AsEntries(message))
                {
                    var added = _store.AddNetwork(entry, connection.TabId);
                    ReportCapture("network", added, connection.TabId);
                }
                break;
            case "selected-element":
                if (JsonUtil.TryGet(message, "element", out var element))
                    _store.SetSelectedElement(element, connection.TabId);
                break;
            case "page":
            {
                var url = JsonUtil.GetString(message, "url");
                lock (_tabLock)
                {
                    TabRecord? record = null;
                    if (connection.TabId is not null)
                        _tabs.TryGetValue(connection.TabId, out record);
                    if (record is not null && url.Length > 0)
                    {
                        record.Url = url;
                        record.LastActivityAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    }

                    if (record is not null && connection.TabId == _currentTabId)
                        _store.SetCurrentPage(url, connection.TabId);
                    else if (record is null)
                        _store.SetCurrentPage(url, JsonUtil.TabIdString(message, "tabId"));
                }
                break;
            }
            case "settings":
                if (JsonUtil.TryGet(message, "settings", out var settings))
                    _store.UpdateSettings(settings);
                break;
            case "screenshot-result":
            case "refresh-result":
            case "storage-result":
            case "run-script-result":
            case "interact-result":
                ResolvePending(connection, message);
                break;
        }
    }

    private void ResolvePending(ExtensionConnection connection, JsonElement message)
    {
        var requestId = JsonUtil.GetString(message, "requestId");
        if (string.IsNullOrEmpty(requestId) || !_pending.TryRemove(requestId, out var pending))
            return;
        if (pending.ConnectionId != connection.Id)
        {
            Log.Warn("connector", "Discarded a response that came from a different tab than was asked");
            return;
        }

        pending.Timeout.Cancel();
        pending.Timeout.Dispose();
        if (JsonUtil.TryGet(message, "ok", out var ok) && ok.ValueKind == JsonValueKind.False)
        {
            var reason = JsonUtil.GetString(message, "error");
            pending.Tcs.TrySetException(new ExtensionRequestException(
                string.IsNullOrEmpty(reason) ? "Extension reported a failure" : reason));
            return;
        }

        pending.Tcs.TrySetResult(message.Clone());
    }

    private async Task<JsonElement> RequestFromExtensionAsync(
        ExtensionConnection connection,
        string type,
        JsonObject payload,
        CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid().ToString();
        var pending = new PendingRequest
        {
            Tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously),
            ConnectionId = connection.Id,
            Timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
        };
        pending.Timeout.CancelAfter(_config.RequestTimeoutMs);
        _pending[requestId] = pending;
        payload["type"] = type;
        payload["requestId"] = requestId;
        await SendJsonAsync(connection, payload);
        try
        {
            return await pending.Tcs.Task.WaitAsync(pending.Timeout.Token);
        }
        catch (OperationCanceledException)
        {
            _pending.TryRemove(requestId, out _);
            throw new ExtensionTimeoutException($"The extension did not answer {type} in time");
        }
    }

    private JsonObject WelcomeMessage()
    {
        var settingsJson = JsonSerializer.Serialize(_store.Settings, BrowserToolsJsonContext.Default.CaptureSettings);
        return new JsonObject
        {
            ["type"] = "welcome",
            ["settings"] = JsonNode.Parse(settingsJson),
            ["serverVersion"] = Constants.ServerVersion
        };
    }

    private async Task HeartbeatLoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_config.HeartbeatIntervalMs));
        try
        {
            while (await timer.WaitForNextTickAsync(_cts.Token))
            {
                foreach (var connection in _connections.Values.ToArray())
                {
                    if (connection.AwaitingPong)
                    {
                        connection.MissedPings += 1;
                        if (connection.MissedPings >= 2)
                        {
                            Log.Warn("connector", "Extension stopped responding to pings; dropping it");
                            try { connection.Socket.Abort(); } catch { /* gone */ }
                            DropConnection(connection.Id);
                            continue;
                        }
                    }

                    connection.AwaitingPong = true;
                    try
                    {
                        await SendJsonAsync(connection, new JsonObject
                        {
                            ["type"] = "ping",
                            ["id"] = Guid.NewGuid().ToString()
                        });
                    }
                    catch
                    {
                        DropConnection(connection.Id);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    private void DropConnection(string id)
    {
        if (!_connections.TryRemove(id, out var connection))
            return;
        foreach (var (requestId, pending) in _pending.ToArray())
        {
            if (pending.ConnectionId != id)
                continue;
            if (_pending.TryRemove(requestId, out var removed))
            {
                removed.Timeout.Dispose();
                removed.Tcs.TrySetException(new ExtensionRequestException(
                    "The DevTools window handling this request was closed."));
            }
        }

        if (connection.TabId is not null)
            DropTab(connection.TabId, id);
        Log.Info("connector", $"Extension disconnected ({_connections.Count} active)");
    }

    private void BindTab(ExtensionConnection connection, string tabId)
    {
        lock (_tabLock)
        {
            connection.TabId = tabId;
            if (_tabs.TryGetValue(tabId, out var existing))
            {
                existing.ConnectionId = connection.Id;
                existing.LastActivityAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }
            else
            {
                _tabs[tabId] = new TabRecord
                {
                    TabId = tabId,
                    ConnectionId = connection.Id,
                    LastActivityAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
            }

            var firstSight = _seenTabIds.Add(tabId);
            if (firstSight || _currentTabId is null)
            {
                _currentTabId = tabId;
                Log.Info("connector", $"Current tab is now {tabId}");
            }
        }
    }

    private void DropTab(string tabId, string connectionId)
    {
        lock (_tabLock)
        {
            if (!_tabs.TryGetValue(tabId, out var record) || record.ConnectionId != connectionId)
                return;
            _tabs.Remove(tabId);
            if (_currentTabId == tabId)
            {
                var next = _tabs.Values.OrderByDescending(t => t.LastActivityAt).FirstOrDefault();
                _currentTabId = next?.TabId;
            }

            if (_tabs.Count == 0)
                _seenTabIds.Clear();
        }
    }

    private ScopeInfo Scope(bool allTabs, string? requested)
    {
        var tabId = allTabs ? null : ResolveTabId(requested);
        return new ScopeInfo(tabId, TabUrl(tabId), tabId is null ? 0 : Math.Max(0, TabCount() - 1));
    }

    private int TabCount()
    {
        lock (_tabLock)
            return _tabs.Count;
    }

    private string TabUrl(string? tabId)
    {
        if (tabId is null)
            return _store.GetCurrentPage().Url;
        lock (_tabLock)
            return _tabs.TryGetValue(tabId, out var record) ? record.Url : "";
    }

    private string? ResolveTabId(string? requested)
    {
        lock (_tabLock)
        {
            if (requested is null)
                return _currentTabId;
            if (!_tabs.ContainsKey(requested))
            {
                throw new UnknownTabException(
                    $"Unknown tab {requested}. {DescribeTabs()} Call listBrowserTabs to see what is live.");
            }

            return _tabs[requested].TabId;
        }
    }

    private ExtensionConnection ConnectionForTab(string? requested)
    {
        var tabId = ResolveTabId(requested);
        if (tabId is null)
        {
            var fallback = _connections.Values
                .Where(c => c.Socket.State == WebSocketState.Open)
                .OrderByDescending(c => c.LastSeen)
                .FirstOrDefault();
            return fallback ?? throw new NoExtensionException();
        }

        lock (_tabLock)
        {
            if (!_tabs.TryGetValue(tabId, out var record) || record.ConnectionId is null
                || !_connections.TryGetValue(record.ConnectionId, out var connection)
                || connection.Socket.State != WebSocketState.Open)
                throw new NoExtensionException();
            return connection;
        }
    }

    private string DescribeTabs()
    {
        lock (_tabLock)
        {
            if (_tabs.Count == 0)
                return "No tabs are connected.";
            return "Connected tabs: " + string.Join(", ", _tabs.Values.Select(t => $"{t.TabId} ({(string.IsNullOrEmpty(t.Url) ? "unknown url" : t.Url)})")) + ".";
        }
    }

    private void ReportCapture(string kind, object? entry, string? tabId)
    {
        if (!_config.Verbose || entry is null)
            return;
        var where = tabId is null ? "" : $" tab {tabId}";
        if (entry is ConsoleEntry console)
            Log.Info("connector", $"· console {console.Level}{where} {Log.Clip(console.Message)}");
        else if (entry is NetworkEntry network)
        {
            var took = network.DurationMs is int ms ? $" ({ms}ms)" : "";
            Log.Info("connector", $"· network {network.Status} {network.Method}{where} {Log.Clip(network.Url)}{took}");
        }
    }

    private static IEnumerable<JsonElement> AsEntries(JsonElement message)
    {
        if (JsonUtil.TryGet(message, "entries", out var entries) && entries.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in entries.EnumerateArray())
                yield return item;
            yield break;
        }

        if (JsonUtil.TryGet(message, "entry", out var entry))
            yield return entry;
    }

    private static async Task SendJsonAsync(ExtensionConnection connection, JsonNode message)
    {
        if (connection.Socket.State != WebSocketState.Open)
            return;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, McpJsonContext.Default.JsonNode);
        await connection.Socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private async Task HandleHttpAsync(HttpListenerContext context, string path)
    {
        if (path == Constants.IdentityPath)
        {
            await WriteJsonAsync(context.Response, new JsonObject
            {
                ["name"] = Constants.ServerName,
                ["version"] = Constants.ServerVersion,
                ["signature"] = Constants.ServerSignature,
                ["port"] = Port,
                ["extensionConnected"] = HasExtension()
            });
            return;
        }

        if (!path.StartsWith(Constants.ApiPrefix, StringComparison.Ordinal))
        {
            context.Response.StatusCode = 404;
            context.Response.Close();
            return;
        }

        var presented = BearerToken(context.Request);
        if (string.IsNullOrEmpty(presented) || !SessionFile.TokensMatch(presented, _token))
        {
            await WriteErrorAsync(context.Response, 401, "Missing or invalid authorization token", "UNAUTHORIZED");
            return;
        }

        try
        {
            await DispatchApiAsync(context, path[Constants.ApiPrefix.Length..]);
        }
        catch (UnknownTabException ex)
        {
            await WriteErrorAsync(context.Response, 404, ex.Message, "UNKNOWN_TAB");
        }
        catch (UnsafePathException ex)
        {
            await WriteErrorAsync(context.Response, 400, ex.Message, "UNSAFE_PATH");
        }
        catch (NoExtensionException ex)
        {
            await WriteErrorAsync(context.Response, 503, ex.Message, "NO_EXTENSION");
        }
        catch (ExtensionTimeoutException ex)
        {
            await WriteErrorAsync(context.Response, 504, ex.Message, "EXTENSION_TIMEOUT");
        }
        catch (ExtensionRequestException ex)
        {
            await WriteErrorAsync(context.Response, 502, ex.Message, "EXTENSION_ERROR");
        }
        catch (AuditUnavailableException ex)
        {
            await WriteErrorAsync(context.Response, 422, ex.Message, "AUDIT_FAILED");
        }
        catch (Exception ex)
        {
            Log.Error("connector", $"Unexpected error: {ex.Message}");
            await WriteErrorAsync(context.Response, 500, ex.Message, "INTERNAL");
        }
    }

    private async Task DispatchApiAsync(HttpListenerContext context, string apiPath)
    {
        var method = context.Request.HttpMethod;
        var query = context.Request.QueryString;
        JsonElement body = default;
        if (method is "POST" or "PUT")
        {
            if (context.Request.ContentLength64 > 10_000_000)
            {
                await WriteErrorAsync(context.Response, 413, "Request body too large", "PAYLOAD_TOO_LARGE");
                return;
            }

            using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
            var text = await reader.ReadToEndAsync();
            if (!string.IsNullOrWhiteSpace(text))
                body = JsonUtil.Parse(text);
        }

        switch ((method, apiPath))
        {
            case ("GET", "/settings"):
                await WriteSettingsAsync(context.Response);
                return;
            case ("POST", "/settings"):
                if (body.ValueKind == JsonValueKind.Object)
                    _store.UpdateSettings(JsonUtil.TryGet(body, "settings", out var patch) ? patch : body);
                await BroadcastSettingsAsync();
                await WriteSettingsAsync(context.Response);
                return;
            case ("GET", "/tabs"):
                await WriteJsonAsync(context.Response, TabsJson());
                return;
            case ("GET", "/page"):
                await WriteJsonAsync(context.Response, PageJson());
                return;
            case ("GET", "/status"):
                await WriteJsonAsync(context.Response, StatusJson());
                return;
            case ("GET", "/console"):
                await WriteJsonAsync(context.Response, ConsoleJson(ParseConsoleQuery(query, body)));
                return;
            case ("GET", "/network"):
                await WriteJsonAsync(context.Response, NetworkJson(ParseNetworkQuery(query, body)));
                return;
            case ("GET", "/selected-element"):
            {
                var tabId = query["tabId"];
                var element = GetSelectedElement(string.IsNullOrEmpty(tabId) ? null : tabId);
                await WriteJsonAsync(context.Response, new JsonObject { ["element"] = element is null ? null : JsonNode.Parse(element.Value.GetRawText()) });
                return;
            }
            case ("POST", "/wipe"):
                Wipe(JsonUtil.TabIdString(body, "tabId"));
                await WriteJsonAsync(context.Response, new JsonObject { ["ok"] = true });
                return;
            case ("POST", "/screenshot"):
            {
                var shot = await CaptureScreenshotAsync(JsonUtil.GetString(body, "name") is { Length: > 0 } n ? n : null, JsonUtil.TabIdString(body, "tabId"), _cts.Token);
                await WriteJsonAsync(context.Response, ScreenshotJson(shot));
                return;
            }
            case ("POST", "/refresh"):
                await RefreshTabAsync(JsonUtil.TabIdString(body, "tabId"), _cts.Token);
                await WriteJsonAsync(context.Response, new JsonObject { ["ok"] = true });
                return;
            case ("POST", "/storage"):
            {
                var kinds = ParseList(body, "kinds") ?? ["localStorage", "sessionStorage"];
                var storage = await ReadStorageAsync(kinds, JsonUtil.TabIdString(body, "tabId"), _cts.Token);
                await WriteJsonAsync(context.Response, new JsonObject { ["storage"] = JsonNode.Parse(storage.GetRawText()) });
                return;
            }
            case ("POST", "/script"):
            {
                var timeout = JsonUtil.GetNumber(body, "timeoutMs");
                var result = await RunPageScriptAsync(JsonUtil.GetString(body, "script"), JsonUtil.TabIdString(body, "tabId"), timeout > 0 ? (int)timeout : null, _cts.Token);
                await WriteJsonAsync(context.Response, ScriptJson(result));
                return;
            }
            case ("POST", "/interact"):
            {
                var result = await InteractAsync(new InteractRequest(
                    JsonUtil.GetString(body, "action"),
                    JsonUtil.GetString(body, "selector") is { Length: > 0 } s ? s : null,
                    JsonUtil.GetString(body, "text") is { Length: > 0 } t ? t : null,
                    JsonUtil.GetString(body, "key") is { Length: > 0 } k ? k : null,
                    NumberOrNull(body, "x"),
                    NumberOrNull(body, "y"),
                    NumberOrNull(body, "deltaX"),
                    NumberOrNull(body, "deltaY"),
                    JsonUtil.TabIdString(body, "tabId")), _cts.Token);
                await WriteJsonAsync(context.Response, InteractJson(result));
                return;
            }
        }

        if (method == "GET" && apiPath.StartsWith("/export/", StringComparison.Ordinal))
        {
            var kind = apiPath["/export/".Length..];
            var allTabs = ParseBool(query["allTabs"]) == true;
            var tabId = query["tabId"];
            if (kind == "console")
            {
                var exported = ExportConsole(string.IsNullOrEmpty(tabId) ? null : tabId, allTabs);
                await WriteJsonAsync(context.Response, ExportJson(exported, e => ConsoleEntryJson(e)));
                return;
            }

            if (kind == "network")
            {
                var exported = ExportNetwork(string.IsNullOrEmpty(tabId) ? null : tabId, allTabs);
                await WriteJsonAsync(context.Response, ExportJson(exported, e => NetworkEntryJson(e)));
                return;
            }

            await WriteErrorAsync(context.Response, 400, $"Unknown export: {kind}", "BAD_EXPORT");
            return;
        }

        if (method == "GET" && apiPath.StartsWith("/artifact/", StringComparison.Ordinal))
        {
            var rest = apiPath["/artifact/".Length..];
            var slash = rest.IndexOf('/');
            if (slash <= 0 || slash == rest.Length - 1)
            {
                await WriteErrorAsync(context.Response, 400, "Artifact path must be /artifact/{kind}/{name}", "BAD_ARTIFACT");
                return;
            }

            var kind = rest[..slash];
            var name = Uri.UnescapeDataString(rest[(slash + 1)..]);
            var artifact = await ReadArtifactAsync(kind, name);
            await WriteJsonAsync(context.Response, new JsonObject
            {
                ["mimeType"] = artifact.MimeType,
                ["text"] = artifact.Text,
                ["blob"] = artifact.Blob
            });
            return;
        }

        if (method == "POST" && apiPath.StartsWith("/audit/", StringComparison.Ordinal))
            throw new AuditUnavailableException();

        context.Response.StatusCode = 404;
        context.Response.Close();
    }

    private async Task BroadcastSettingsAsync()
    {
        var settingsJson = JsonSerializer.Serialize(_store.Settings, BrowserToolsJsonContext.Default.CaptureSettings);
        var message = new JsonObject { ["type"] = "settings", ["settings"] = JsonNode.Parse(settingsJson) };
        foreach (var connection in _connections.Values)
            await SendJsonAsync(connection, message);
    }

    private Task WriteSettingsAsync(HttpListenerResponse response)
    {
        var settingsJson = JsonSerializer.Serialize(_store.Settings, BrowserToolsJsonContext.Default.CaptureSettings);
        return WriteJsonAsync(response, new JsonObject { ["settings"] = JsonNode.Parse(settingsJson) });
    }

    private JsonObject TabsJson()
    {
        var tabs = new JsonArray();
        foreach (var tab in ListTabs())
        {
            JsonUtil.Push(tabs, new JsonObject
            {
                ["tabId"] = JsonUtil.TabIdNode(tab.TabId),
                ["url"] = tab.Url,
                ["isCurrent"] = tab.IsCurrent,
                ["consoleCount"] = tab.ConsoleCount,
                ["networkCount"] = tab.NetworkCount
            });
        }

        return new JsonObject
        {
            ["tabs"] = tabs,
            ["currentTabId"] = JsonUtil.TabIdNode(GetCurrentTabId()),
            ["connectedTabs"] = tabs.Count
        };
    }

    private JsonObject PageJson()
    {
        var tabs = ListTabs();
        var current = tabs.FirstOrDefault(t => t.IsCurrent);
        var page = _store.GetCurrentPage();
        return new JsonObject
        {
            ["url"] = current?.Url ?? page.Url,
            ["tabId"] = JsonUtil.TabIdNode(GetCurrentTabId() ?? page.TabId),
            ["extensionConnected"] = HasExtension(),
            ["connectedTabs"] = tabs.Count
        };
    }

    public JsonObject StatusJson()
    {
        var tabs = ListTabs();
        return new JsonObject
        {
            ["version"] = Constants.ServerVersion,
            ["extensionConnected"] = HasExtension(),
            ["connections"] = _connections.Count,
            ["tabs"] = tabs.Count,
            ["currentTabId"] = JsonUtil.TabIdNode(GetCurrentTabId()),
            ["screenshotDir"] = _screenshotDir,
            ["counts"] = new JsonObject
            {
                ["console"] = _store.QueryConsole(new ConsoleQuery()).Total,
                ["network"] = _store.QueryNetwork(new NetworkQuery()).Total
            }
        };
    }

    public JsonObject ConsoleJson(ConsoleQuery query) => QueryJson(QueryConsole(query), ConsoleEntryJson);

    public JsonObject NetworkJson(NetworkQuery query) => QueryJson(QueryNetwork(query), NetworkEntryJson);

    public static JsonObject QueryJsonPublic<T>(TabScopedResult<T> result, Func<T, JsonObject> map)
        => QueryJson(result, map);

    private static JsonObject QueryJson<T>(TabScopedResult<T> result, Func<T, JsonObject> map)
    {
        var entries = new JsonArray();
        foreach (var entry in result.Entries)
            JsonUtil.Push(entries, map(entry));
        return new JsonObject
        {
            ["entries"] = entries,
            ["total"] = result.Total,
            ["returned"] = result.Returned,
            ["truncated"] = result.Truncated,
            ["tabId"] = JsonUtil.TabIdNode(result.TabId),
            ["url"] = result.Url,
            ["otherTabs"] = result.OtherTabs
        };
    }

    private static JsonObject ExportJson<T>(ExportBundle<T> exported, Func<T, JsonObject> map)
    {
        var entries = new JsonArray();
        foreach (var entry in exported.Entries)
            JsonUtil.Push(entries, map(entry));
        return new JsonObject
        {
            ["tabId"] = JsonUtil.TabIdNode(exported.TabId),
            ["url"] = exported.Url,
            ["entries"] = entries
        };
    }

    internal static JsonObject ConsoleEntryJson(ConsoleEntry entry)
    {
        var obj = new JsonObject
        {
            ["type"] = entry.Type,
            ["level"] = entry.Level,
            ["message"] = entry.Message,
            ["timestamp"] = entry.Timestamp
        };
        if (entry.TabId is not null) obj["tabId"] = JsonUtil.TabIdNode(entry.TabId);
        if (entry.Url is not null) obj["url"] = entry.Url;
        if (entry.StackTrace is { } stack)
            obj["stackTrace"] = JsonNode.Parse(stack.GetRawText());
        return obj;
    }

    internal static JsonObject NetworkEntryJson(NetworkEntry entry)
    {
        var obj = new JsonObject
        {
            ["type"] = entry.Type,
            ["url"] = entry.Url,
            ["method"] = entry.Method,
            ["status"] = entry.Status,
            ["timestamp"] = entry.Timestamp
        };
        if (entry.StartedAt is not null) obj["startedAt"] = entry.StartedAt;
        if (entry.TabId is not null) obj["tabId"] = JsonUtil.TabIdNode(entry.TabId);
        if (entry.DurationMs is not null) obj["durationMs"] = entry.DurationMs;
        if (entry.Error is not null) obj["error"] = entry.Error;
        if (entry.RequestHeaders is not null) obj["requestHeaders"] = HeadersJson(entry.RequestHeaders);
        if (entry.ResponseHeaders is not null) obj["responseHeaders"] = HeadersJson(entry.ResponseHeaders);
        if (entry.RequestBody is not null) obj["requestBody"] = entry.RequestBody;
        if (entry.ResponseBody is not null) obj["responseBody"] = entry.ResponseBody;
        return obj;
    }

    private static JsonObject HeadersJson(Dictionary<string, string> headers)
    {
        var obj = new JsonObject();
        foreach (var (name, value) in headers)
            obj[name] = value;
        return obj;
    }

    internal static JsonObject ScreenshotJson(ScreenshotCapture shot) => new()
    {
        ["path"] = shot.Path,
        ["data"] = shot.Data,
        ["name"] = shot.Name,
        ["mimeType"] = shot.MimeType,
        ["bytes"] = shot.Bytes,
        ["withinBudget"] = shot.WithinBudget,
        ["tabId"] = JsonUtil.TabIdNode(shot.TabId),
        ["url"] = shot.Url
    };

    internal static JsonObject ScriptJson(PageScriptResult result) => new()
    {
        ["result"] = JsonNode.Parse(result.Result.GetRawText()),
        ["resultType"] = result.ResultType,
        ["awaited"] = result.Awaited,
        ["truncated"] = result.Truncated,
        ["tabId"] = JsonUtil.TabIdNode(result.TabId),
        ["url"] = result.Url,
        ["otherTabs"] = result.OtherTabs
    };

    internal static JsonObject InteractJson(InteractResult result)
    {
        var obj = new JsonObject
        {
            ["action"] = result.Action,
            ["matched"] = result.Matched,
            ["tabId"] = JsonUtil.TabIdNode(result.TabId),
            ["url"] = result.Url,
            ["otherTabs"] = result.OtherTabs
        };
        if (result.X is not null) obj["x"] = result.X;
        if (result.Y is not null) obj["y"] = result.Y;
        if (result.TagName is not null) obj["tagName"] = result.TagName;
        return obj;
    }

    private static ConsoleQuery ParseConsoleQuery(System.Collections.Specialized.NameValueCollection query, JsonElement body)
        => new()
        {
            ErrorsOnly = ParseBool(query["errorsOnly"]) == true,
            Keywords = ParseList(query["keywords"]),
            TabId = string.IsNullOrEmpty(query["tabId"]) ? JsonUtil.TabIdString(body, "tabId") : query["tabId"],
            AllTabs = ParseBool(query["allTabs"]) == true,
            Limit = ParseCount(query["limit"]),
            Offset = ParseCount(query["offset"])
        };

    private static NetworkQuery ParseNetworkQuery(System.Collections.Specialized.NameValueCollection query, JsonElement body)
        => new()
        {
            ErrorsOnly = ParseBool(query["errorsOnly"]) == true,
            UrlKeywords = ParseList(query["urlKeywords"]),
            BodyKeywords = ParseList(query["bodyKeywords"]),
            TabId = string.IsNullOrEmpty(query["tabId"]) ? JsonUtil.TabIdString(body, "tabId") : query["tabId"],
            AllTabs = ParseBool(query["allTabs"]) == true,
            Limit = ParseCount(query["limit"]),
            Offset = ParseCount(query["offset"])
        };

    private static List<string>? ParseList(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return null;
        var items = raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return items.Length == 0 ? null : [.. items];
    }

    private static List<string>? ParseList(JsonElement body, string name)
    {
        if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty(name, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.Array)
        {
            var items = value.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList();
            return items.Count == 0 ? null : items;
        }

        return value.ValueKind == JsonValueKind.String ? ParseList(value.GetString()) : null;
    }

    private static bool? ParseBool(string? value)
        => value?.ToLowerInvariant() switch
        {
            "true" or "1" or "yes" => true,
            "false" or "0" or "no" => false,
            _ => null
        };

    private static int? ParseCount(string? value)
        => int.TryParse(value, out var n) && n >= 0 ? n : null;

    private static double? NumberOrNull(JsonElement obj, string name)
        => JsonUtil.TryGet(obj, name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;

    private static string BearerToken(HttpListenerRequest request)
    {
        var header = request.Headers["Authorization"] ?? "";
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return header["Bearer ".Length..].Trim();
        return request.QueryString["token"] ?? "";
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, JsonNode node)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(node, McpJsonContext.Default.JsonNode);
        response.StatusCode = 200;
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    private static async Task WriteErrorAsync(HttpListenerResponse response, int status, string message, string code)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            new JsonObject { ["error"] = message, ["code"] = code },
            McpJsonContext.Default.JsonNode);
        response.StatusCode = status;
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    private sealed class ExtensionConnection
    {
        public required string Id { get; init; }
        public required HttpListenerWebSocketContext Context { get; init; }
        public WebSocket Socket => Context.WebSocket;
        public string? TabId { get; set; }
        public bool AwaitingPong { get; set; }
        public int MissedPings { get; set; }
        public long LastSeen { get; set; }
    }

    private sealed class PendingRequest
    {
        public required TaskCompletionSource<JsonElement> Tcs { get; init; }
        public required string ConnectionId { get; init; }
        public required CancellationTokenSource Timeout { get; init; }
    }

    private sealed class TabRecord
    {
        public required string TabId { get; init; }
        public string? ConnectionId { get; set; }
        public string Url { get; set; } = "";
        public long LastActivityAt { get; set; }
    }
}

public readonly record struct ScopeInfo(string? TabId, string Url, int OtherTabs);

public readonly record struct TabScopedResult<T>(
    IReadOnlyList<T> Entries,
    int Total,
    int Returned,
    bool Truncated,
    string? TabId,
    string Url,
    int OtherTabs)
{
    public static TabScopedResult<T> From(QueryResult<T> result, ScopeInfo scope)
        => new(result.Entries, result.Total, result.Returned, result.Truncated, scope.TabId, scope.Url, scope.OtherTabs);
}

public readonly record struct ExportBundle<T>(string? TabId, string Url, IReadOnlyList<T> Entries);

public readonly record struct ScreenshotCapture(
    string Path,
    string Data,
    string Name,
    string MimeType,
    int Bytes,
    bool WithinBudget,
    string? TabId,
    string Url);

public readonly record struct Artifact(string MimeType, string? Text, string? Blob);

public readonly record struct PageScriptResult(
    JsonElement Result,
    string ResultType,
    bool Awaited,
    bool Truncated,
    string? TabId,
    string Url,
    int OtherTabs);

public readonly record struct InteractRequest(
    string Action,
    string? Selector,
    string? Text,
    string? Key,
    double? X,
    double? Y,
    double? DeltaX,
    double? DeltaY,
    string? TabId);

public readonly record struct InteractResult(
    string Action,
    int Matched,
    double? X,
    double? Y,
    string? TagName,
    string? TabId,
    string Url,
    int OtherTabs);
