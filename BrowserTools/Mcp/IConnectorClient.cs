using System.Text.Json;
using System.Text.Json.Nodes;

namespace BrowserTools.Mcp;

public interface IConnectorClient
{
    Task<TabScopedResult<ConsoleEntry>> ConsoleAsync(ConsoleQuery query);
    Task<TabScopedResult<NetworkEntry>> NetworkAsync(NetworkQuery query);
    Task<JsonElement?> SelectedElementAsync(string? tabId);
    Task<JsonObject> PageAsync();
    Task<JsonObject> StatusAsync();
    Task<JsonObject> TabsAsync();
    Task WipeAsync(string? tabId);
    Task<ScreenshotCapture> ScreenshotAsync(string? name, string? tabId);
    Task RefreshAsync(string? tabId);
    Task<JsonElement> StorageAsync(IReadOnlyList<string> kinds, string? tabId);
    Task<PageScriptResult> RunPageScriptAsync(string script, string? tabId, int? timeoutMs);
    Task<InteractResult> InteractAsync(InteractRequest request);
    Task<ExportBundle<ConsoleEntry>> ExportConsoleAsync(string? tabId, bool allTabs);
    Task<ExportBundle<NetworkEntry>> ExportNetworkAsync(string? tabId, bool allTabs);
    Task<Artifact> ReadArtifactAsync(string kind, string name);
}

public sealed class InProcessConnectorClient : IConnectorClient
{
    private readonly Connector _connector;

    public InProcessConnectorClient(Connector connector) => _connector = connector;

    public Task<TabScopedResult<ConsoleEntry>> ConsoleAsync(ConsoleQuery query)
        => Task.FromResult(_connector.QueryConsole(query));

    public Task<TabScopedResult<NetworkEntry>> NetworkAsync(NetworkQuery query)
        => Task.FromResult(_connector.QueryNetwork(query));

    public Task<JsonElement?> SelectedElementAsync(string? tabId)
        => Task.FromResult(_connector.GetSelectedElement(tabId));

    public Task<JsonObject> PageAsync()
    {
        var tabs = _connector.ListTabs();
        var current = tabs.FirstOrDefault(t => t.IsCurrent);
        var page = _connector.Store.GetCurrentPage();
        return Task.FromResult(new JsonObject
        {
            ["url"] = current?.Url ?? page.Url,
            ["tabId"] = JsonUtil.TabIdNode(_connector.GetCurrentTabId() ?? page.TabId),
            ["extensionConnected"] = _connector.HasExtension(),
            ["connectedTabs"] = tabs.Count
        });
    }

    public Task<JsonObject> StatusAsync() => Task.FromResult(_connector.StatusJson());

    public Task<JsonObject> TabsAsync()
    {
        var tabs = new JsonArray();
        foreach (var tab in _connector.ListTabs())
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

        return Task.FromResult(new JsonObject
        {
            ["tabs"] = tabs,
            ["currentTabId"] = JsonUtil.TabIdNode(_connector.GetCurrentTabId()),
            ["connectedTabs"] = tabs.Count
        });
    }

    public Task WipeAsync(string? tabId)
    {
        _connector.Wipe(tabId);
        return Task.CompletedTask;
    }

    public Task<ScreenshotCapture> ScreenshotAsync(string? name, string? tabId)
        => _connector.CaptureScreenshotAsync(name, tabId, CancellationToken.None);

    public Task RefreshAsync(string? tabId)
        => _connector.RefreshTabAsync(tabId, CancellationToken.None);

    public Task<JsonElement> StorageAsync(IReadOnlyList<string> kinds, string? tabId)
        => _connector.ReadStorageAsync(kinds, tabId, CancellationToken.None);

    public Task<PageScriptResult> RunPageScriptAsync(string script, string? tabId, int? timeoutMs)
        => _connector.RunPageScriptAsync(script, tabId, timeoutMs, CancellationToken.None);

    public Task<InteractResult> InteractAsync(InteractRequest request)
        => _connector.InteractAsync(request, CancellationToken.None);

    public Task<ExportBundle<ConsoleEntry>> ExportConsoleAsync(string? tabId, bool allTabs)
        => Task.FromResult(_connector.ExportConsole(tabId, allTabs));

    public Task<ExportBundle<NetworkEntry>> ExportNetworkAsync(string? tabId, bool allTabs)
        => Task.FromResult(_connector.ExportNetwork(tabId, allTabs));

    public Task<Artifact> ReadArtifactAsync(string kind, string name)
        => _connector.ReadArtifactAsync(kind, name);
}

public sealed class UnavailableConnectorClient : IConnectorClient
{
    private readonly string _reason;

    public UnavailableConnectorClient(string reason) => _reason = reason;

    private InvalidOperationException Error()
        => new(
            $"The browser connector is not available: {_reason}. Check that nothing else is using the port, then restart your MCP client.");

    private Task<T> Fail<T>() => Task.FromException<T>(Error());

    public Task<TabScopedResult<ConsoleEntry>> ConsoleAsync(ConsoleQuery query) => Fail<TabScopedResult<ConsoleEntry>>();
    public Task<TabScopedResult<NetworkEntry>> NetworkAsync(NetworkQuery query) => Fail<TabScopedResult<NetworkEntry>>();
    public Task<JsonElement?> SelectedElementAsync(string? tabId) => Fail<JsonElement?>();
    public Task<JsonObject> PageAsync() => Fail<JsonObject>();
    public Task<JsonObject> StatusAsync() => Fail<JsonObject>();
    public Task<JsonObject> TabsAsync() => Fail<JsonObject>();
    public Task WipeAsync(string? tabId) => Task.FromException(Error());
    public Task<ScreenshotCapture> ScreenshotAsync(string? name, string? tabId) => Fail<ScreenshotCapture>();
    public Task RefreshAsync(string? tabId) => Task.FromException(Error());
    public Task<JsonElement> StorageAsync(IReadOnlyList<string> kinds, string? tabId) => Fail<JsonElement>();
    public Task<PageScriptResult> RunPageScriptAsync(string script, string? tabId, int? timeoutMs) => Fail<PageScriptResult>();
    public Task<InteractResult> InteractAsync(InteractRequest request) => Fail<InteractResult>();
    public Task<ExportBundle<ConsoleEntry>> ExportConsoleAsync(string? tabId, bool allTabs) => Fail<ExportBundle<ConsoleEntry>>();
    public Task<ExportBundle<NetworkEntry>> ExportNetworkAsync(string? tabId, bool allTabs) => Fail<ExportBundle<NetworkEntry>>();
    public Task<Artifact> ReadArtifactAsync(string kind, string name) => Fail<Artifact>();
}
