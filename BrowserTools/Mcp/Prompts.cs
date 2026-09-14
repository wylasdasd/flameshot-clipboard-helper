using System.Text.Json.Nodes;

namespace BrowserTools.Mcp;

public static class Prompts
{
    public static JsonArray List()
    {
        var arr = new JsonArray();
        JsonUtil.Push(arr, Item("debuggerMode", "Debug this page",
            "A systematic workflow for diagnosing a problem on the live page using console and network telemetry."));
        JsonUtil.Push(arr, Item("auditMode", "Audit this page",
            "A workflow for running and interpreting accessibility, performance, SEO and best-practices audits."));
        JsonUtil.Push(arr, Item("nextjsSeoAudit", "Next.js SEO audit",
            "SEO review tailored to Next.js applications, combining a live SEO audit with framework-specific causes."));
        return arr;
    }

    public static string? GetText(string name) => name switch
    {
        "debuggerMode" => DebuggerMode,
        "auditMode" => AuditMode,
        "nextjsSeoAudit" => NextJsSeo,
        _ => null
    };

    private static JsonObject Item(string name, string title, string description) => new()
    {
        ["name"] = name,
        ["title"] = title,
        ["description"] = description
    };

    private const string DebuggerMode =
        """
        You are debugging a live web page using BrowserTools MCP. Work through the evidence before changing code.

        1. Call getConnectionStatus first. If no extension is connected, stop and tell the user to open Chrome DevTools (F12).
        2. Call getPageInfo to confirm which page is being inspected. If connectedTabs is above 1, call listBrowserTabs.
        3. Gather evidence: getConsoleErrors, getNetworkErrors, then targeted getConsoleLogs.
        4. Form a hypothesis and say what evidence supports it.
        5. Before proposing a fix, call wipeLogs, ask the user to reproduce, then re-read the logs.
        """;

    private const string AuditMode =
        """
        You are running a quality audit of a live page using BrowserTools MCP.

        1. Call getPageInfo and confirm this is the page the user wants audited.
        2. Lighthouse audit tools are not available in this native build. Use console, network, selected element, screenshots and page scripts instead.
        3. Report findings grouped by impact, highest first.
        """;

    private const string NextJsSeo =
        """
        You are auditing a Next.js application for SEO using BrowserTools MCP.

        Live Lighthouse SEO scores are not available in this native build. Inspect the current page with getPageInfo, getNetworkLogs and getSelectedElement, then review App Router metadata, sitemap, robots and server-rendered content in the codebase.
        """;
}
