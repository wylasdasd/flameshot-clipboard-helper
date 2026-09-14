using System.Text.Json;
using System.Text.Json.Nodes;

namespace BrowserTools.Mcp;

public static class ToolRouter
{
    public static readonly string[] ToolNames =
    [
        "listBrowserTabs",
        "getConsoleLogs",
        "getConsoleErrors",
        "getNetworkLogs",
        "getNetworkErrors",
        "getSelectedElement",
        "getPageInfo",
        "getConnectionStatus",
        "takeScreenshot",
        "refreshBrowser",
        "getBrowserStorage",
        "wipeLogs",
        "runPageScript",
        "interactWithPage"
    ];

    public static JsonArray ListTools()
    {
        var tools = new JsonArray();
        Add(tools, "listBrowserTabs", "Every browser tab that currently has DevTools open.", new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }, true);
        Add(tools, "getConsoleLogs", "Console output from the current tab.", QuerySchema(true, false), true);
        Add(tools, "getConsoleErrors", "Only error-level console output from the current tab.", QuerySchema(true, false), true);
        Add(tools, "getNetworkLogs", "XHR and fetch requests from the current tab.", QuerySchema(false, true), true);
        Add(tools, "getNetworkErrors", "Only failed or 4xx/5xx requests.", QuerySchema(false, true), true);
        Add(tools, "getSelectedElement", "The DOM element selected in the Elements panel.", TabOnlySchema(), true);
        Add(tools, "getPageInfo", "Which page the browser is currently on.", new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }, true);
        Add(tools, "getConnectionStatus", "Whether the Chrome extension is connected.", new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }, true);
        Add(tools, "takeScreenshot", "Captures the visible area of the current tab.", new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["name"] = new JsonObject { ["type"] = "string" },
                ["tabId"] = TabIdSchema()
            }
        }, false);
        Add(tools, "refreshBrowser", "Reloads the inspected tab.", TabOnlySchema(), false);
        Add(tools, "getBrowserStorage", "Lists localStorage, sessionStorage and cookies.", new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["kinds"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
                ["includeValues"] = new JsonObject { ["type"] = "boolean" },
                ["tabId"] = TabIdSchema()
            }
        }, true);
        Add(tools, "wipeLogs", "Discards captured console and network entries.", TabOnlySchema(), false);
        Add(tools, "runPageScript", "Evaluates an async function body in the inspected page.", new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["script"] = new JsonObject { ["type"] = "string" },
                ["timeoutMs"] = new JsonObject { ["type"] = "integer" },
                ["tabId"] = TabIdSchema()
            },
            ["required"] = StringArray("script")
        }, false);
        Add(tools, "interactWithPage", "Synthesizes mouse and keyboard input via the DevTools protocol.", new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["action"] = new JsonObject { ["type"] = "string" },
                ["selector"] = new JsonObject { ["type"] = "string" },
                ["text"] = new JsonObject { ["type"] = "string" },
                ["key"] = new JsonObject { ["type"] = "string" },
                ["x"] = new JsonObject { ["type"] = "number" },
                ["y"] = new JsonObject { ["type"] = "number" },
                ["deltaX"] = new JsonObject { ["type"] = "number" },
                ["deltaY"] = new JsonObject { ["type"] = "number" },
                ["tabId"] = TabIdSchema()
            },
            ["required"] = StringArray("action")
        }, false);
        return tools;
    }

    public static async Task<JsonObject> CallAsync(IConnectorClient client, string name, JsonElement args)
    {
        try
        {
            return name switch
            {
                "listBrowserTabs" => Ok(await client.TabsAsync()),
                "getConsoleLogs" => Ok(ConsoleJson(await client.ConsoleAsync(ParseConsole(args, false)))),
                "getConsoleErrors" => Ok(ConsoleJson(await client.ConsoleAsync(ParseConsole(args, true)))),
                "getNetworkLogs" => Ok(NetworkJson(await client.NetworkAsync(ParseNetwork(args, false)))),
                "getNetworkErrors" => Ok(NetworkJson(await client.NetworkAsync(ParseNetwork(args, true)))),
                "getSelectedElement" => Ok(new JsonObject
                {
                    ["element"] = NodeOrNull(await client.SelectedElementAsync(TabId(args)))
                }),
                "getPageInfo" => Ok(await client.PageAsync()),
                "getConnectionStatus" => Ok(await client.StatusAsync()),
                "takeScreenshot" => await ScreenshotAsync(client, args),
                "refreshBrowser" => await RefreshAsync(client, args),
                "getBrowserStorage" => await StorageAsync(client, args),
                "wipeLogs" => await WipeAsync(client, args),
                "runPageScript" => Ok(Connector.ScriptJson(await client.RunPageScriptAsync(
                    JsonUtil.GetString(args, "script"), TabId(args),
                    TimeoutMs(args)))),
                "interactWithPage" => Ok(Connector.InteractJson(await client.InteractAsync(new InteractRequest(
                    JsonUtil.GetString(args, "action"),
                    EmptyToNull(JsonUtil.GetString(args, "selector")),
                    EmptyToNull(JsonUtil.GetString(args, "text")),
                    EmptyToNull(JsonUtil.GetString(args, "key")),
                    Number(args, "x"), Number(args, "y"), Number(args, "deltaX"), Number(args, "deltaY"),
                    TabId(args))))),
                _ => Fail($"Unknown tool: {name}")
            };
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    public static async Task<JsonObject?> ReadResourceAsync(IConnectorClient client, string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || parsed.Scheme != "browser-tools")
            return null;

        var host = parsed.Host;
        var name = Uri.UnescapeDataString(parsed.AbsolutePath.Trim('/'));
        switch (host)
        {
            case "console":
            {
                var scope = ScopeOf(name);
                var exported = await client.ExportConsoleAsync(scope.TabId, scope.AllTabs);
                var entries = new JsonArray();
                foreach (var entry in exported.Entries)
                    JsonUtil.Push(entries, Connector.ConsoleEntryJson(entry));
                return ResourceJson(uri, "application/json", JsonUtil.Serialize(new JsonObject
                {
                    ["tabId"] = JsonUtil.TabIdNode(exported.TabId),
                    ["url"] = exported.Url,
                    ["entries"] = entries
                }));
            }
            case "network":
            {
                var scope = ScopeOf(name);
                var exported = await client.ExportNetworkAsync(scope.TabId, scope.AllTabs);
                var entries = new JsonArray();
                foreach (var entry in exported.Entries)
                    JsonUtil.Push(entries, Connector.NetworkEntryJson(entry));
                return ResourceJson(uri, "application/json", JsonUtil.Serialize(new JsonObject
                {
                    ["tabId"] = JsonUtil.TabIdNode(exported.TabId),
                    ["url"] = exported.Url,
                    ["entries"] = entries
                }));
            }
            case "har":
            {
                var scope = ScopeOf(name);
                var exported = await client.ExportNetworkAsync(scope.TabId, scope.AllTabs);
                return ResourceJson(uri, "application/json", JsonUtil.Serialize(HarBuilder.Build(exported.Entries.ToList())));
            }
            case "screenshot":
            {
                var artifact = await client.ReadArtifactAsync("screenshot", name);
                return ResourceBlob(uri, artifact);
            }
            case "audit":
            {
                var artifact = await client.ReadArtifactAsync("audit", name);
                return ResourceJson(uri, artifact.MimeType, artifact.Text ?? "");
            }
            default:
                return null;
        }
    }

    public static JsonArray ListResourceTemplates()
    {
        var arr = new JsonArray();
        JsonUtil.Push(arr, Template("browser-tools://console/{scope}", "Full console history"));
        JsonUtil.Push(arr, Template("browser-tools://network/{scope}", "Full network history"));
        JsonUtil.Push(arr, Template("browser-tools://har/{scope}", "Network activity as HAR"));
        JsonUtil.Push(arr, Template("browser-tools://screenshot/{name}", "Captured screenshot"));
        JsonUtil.Push(arr, Template("browser-tools://audit/{reportId}", "Full Lighthouse report"));
        return arr;
    }

    private static async Task<JsonObject> ScreenshotAsync(IConnectorClient client, JsonElement args)
    {
        var shot = await client.ScreenshotAsync(EmptyToNull(JsonUtil.GetString(args, "name")), TabId(args));
        var structured = new JsonObject
        {
            ["path"] = shot.Path,
            ["name"] = shot.Name,
            ["mimeType"] = shot.MimeType,
            ["bytes"] = shot.Bytes,
            ["imageIncluded"] = shot.WithinBudget,
            ["tabId"] = JsonUtil.TabIdNode(shot.TabId),
            ["url"] = shot.Url
        };
        var extra = new JsonArray();
        JsonUtil.Push(extra, new JsonObject
        {
            ["type"] = "resource_link",
            ["uri"] = $"browser-tools://screenshot/{shot.Name}",
            ["name"] = shot.Name,
            ["mimeType"] = shot.MimeType
        });
        if (shot.WithinBudget)
        {
            var base64 = shot.Data.Contains(',') ? shot.Data[(shot.Data.IndexOf(',') + 1)..] : shot.Data;
            JsonUtil.Push(extra, new JsonObject { ["type"] = "image", ["data"] = base64, ["mimeType"] = shot.MimeType });
        }
        else
        {
            JsonUtil.Push(extra, new JsonObject
            {
                ["type"] = "text",
                ["text"] = $"The screenshot is {shot.Bytes / 1024} KB, too large to include here. It was saved to {shot.Path}."
            });
        }

        return Ok(structured, extra);
    }

    private static async Task<JsonObject> RefreshAsync(IConnectorClient client, JsonElement args)
    {
        await client.RefreshAsync(TabId(args));
        return Ok(new JsonObject { ["ok"] = true });
    }

    private static async Task<JsonObject> WipeAsync(IConnectorClient client, JsonElement args)
    {
        await client.WipeAsync(TabId(args));
        return Ok(new JsonObject { ["ok"] = true });
    }

    private static async Task<JsonObject> StorageAsync(IConnectorClient client, JsonElement args)
    {
        var kinds = ParseStringList(args, "kinds") ?? ["localStorage", "sessionStorage"];
        var raw = await client.StorageAsync(kinds, TabId(args));
        var includeValues = JsonUtil.TryGet(args, "includeValues", out var flag) && flag.ValueKind == JsonValueKind.True;
        return Ok(new JsonObject
        {
            ["storage"] = includeValues ? JsonNode.Parse(raw.GetRawText()) : SummariseStorage(raw),
            ["includedValues"] = includeValues
        });
    }

    private static JsonObject SummariseStorage(JsonElement storage)
    {
        var outObj = new JsonObject();
        if (storage.ValueKind != JsonValueKind.Object)
            return outObj;
        foreach (var property in storage.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Array)
            {
                var names = new JsonArray();
                foreach (var item in property.Value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("name", out var n))
                        JsonUtil.Push(names, n.GetString());
                }

                outObj[property.Name] = new JsonObject { ["names"] = names, ["count"] = property.Value.GetArrayLength() };
            }
            else if (property.Value.ValueKind == JsonValueKind.Object)
            {
                var keys = new JsonArray();
                foreach (var key in property.Value.EnumerateObject())
                    JsonUtil.Push(keys, key.Name);
                outObj[property.Name] = new JsonObject { ["keys"] = keys, ["count"] = keys.Count };
            }
            else
            {
                outObj[property.Name] = new JsonObject { ["keys"] = new JsonArray(), ["count"] = 0 };
            }
        }

        return outObj;
    }

    private static JsonObject ConsoleJson(TabScopedResult<ConsoleEntry> result)
        => Connector.QueryJsonPublic(result, Connector.ConsoleEntryJson);

    private static JsonObject NetworkJson(TabScopedResult<NetworkEntry> result)
        => Connector.QueryJsonPublic(result, Connector.NetworkEntryJson);

    private static ConsoleQuery ParseConsole(JsonElement args, bool errorsOnly) => new()
    {
        ErrorsOnly = errorsOnly,
        Keywords = ParseStringList(args, "keywords"),
        TabId = TabId(args),
        AllTabs = IsTrue(args, "allTabs"),
        Limit = Int(args, "limit"),
        Offset = Int(args, "offset")
    };

    private static NetworkQuery ParseNetwork(JsonElement args, bool errorsOnly) => new()
    {
        ErrorsOnly = errorsOnly,
        UrlKeywords = ParseStringList(args, "urlKeywords"),
        BodyKeywords = ParseStringList(args, "bodyKeywords"),
        TabId = TabId(args),
        AllTabs = IsTrue(args, "allTabs"),
        Limit = Int(args, "limit"),
        Offset = Int(args, "offset")
    };

    private static List<string>? ParseStringList(JsonElement args, string name)
    {
        if (!JsonUtil.TryGet(args, name, out var value) || value.ValueKind != JsonValueKind.Array)
            return null;
        var items = value.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList();
        return items.Count == 0 ? null : items;
    }

    private static string? TabId(JsonElement args) => JsonUtil.TabIdString(args, "tabId");
    private static bool IsTrue(JsonElement args, string name)
        => JsonUtil.TryGet(args, name, out var value) && value.ValueKind == JsonValueKind.True;
    private static int? Int(JsonElement args, string name)
    {
        var n = JsonUtil.GetNumber(args, name);
        return n > 0 ? (int)n : null;
    }

    private static int? TimeoutMs(JsonElement args)
    {
        var n = JsonUtil.GetNumber(args, "timeoutMs");
        return n > 0 ? (int)n : null;
    }
    private static double? Number(JsonElement args, string name)
        => JsonUtil.TryGet(args, name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;
    private static string? EmptyToNull(string value) => string.IsNullOrEmpty(value) ? null : value;

    private static JsonNode? NodeOrNull(JsonElement? element)
        => element is null ? null : JsonNode.Parse(element.Value.GetRawText());

    private static (string? TabId, bool AllTabs) ScopeOf(string scope)
        => scope is "" or "all" ? (null, true) : (scope, false);

    private static JsonObject Ok(JsonObject structured, JsonArray? extra = null)
    {
        var content = new JsonArray();
        JsonUtil.Push(content, new JsonObject
        {
            ["type"] = "text",
            ["text"] = JsonUtil.Serialize(structured)
        });
        if (extra is not null)
        {
            foreach (var item in extra)
                JsonUtil.Push(content, item?.DeepClone());
        }

        return new JsonObject
        {
            ["content"] = content,
            ["structuredContent"] = structured
        };
    }

    private static JsonObject Fail(string message)
    {
        var content = new JsonArray();
        JsonUtil.Push(content, new JsonObject { ["type"] = "text", ["text"] = message });
        return new JsonObject { ["content"] = content, ["isError"] = true };
    }

    private static JsonObject ResourceJson(string uri, string mime, string text)
    {
        var contents = new JsonArray();
        JsonUtil.Push(contents, new JsonObject { ["uri"] = uri, ["mimeType"] = mime, ["text"] = text });
        return new JsonObject { ["contents"] = contents };
    }

    private static JsonObject ResourceBlob(string uri, Artifact artifact)
    {
        var item = new JsonObject { ["uri"] = uri, ["mimeType"] = artifact.MimeType };
        if (artifact.Blob is not null)
            item["blob"] = artifact.Blob;
        else
            item["text"] = artifact.Text ?? "";
        var contents = new JsonArray();
        JsonUtil.Push(contents, item);
        return new JsonObject { ["contents"] = contents };
    }

    private static JsonArray StringArray(string value)
    {
        var arr = new JsonArray();
        JsonUtil.Push(arr, value);
        return arr;
    }

    private static JsonObject Template(string uri, string description) => new()
    {
        ["uriTemplate"] = uri,
        ["name"] = uri,
        ["description"] = description,
        ["mimeType"] = "application/json"
    };

    private static void Add(JsonArray tools, string name, string description, JsonObject schema, bool readOnly)
    {
        JsonUtil.Push(tools, new JsonObject
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = schema,
            ["annotations"] = new JsonObject { ["readOnlyHint"] = readOnly }
        });
    }

    private static JsonObject TabIdSchema() => new()
    {
        ["description"] = "Which browser tab to use, from listBrowserTabs."
    };

    private static JsonObject TabOnlySchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject { ["tabId"] = TabIdSchema() }
    };

    private static JsonObject QuerySchema(bool keywords, bool urlKeywords)
    {
        var properties = new JsonObject
        {
            ["tabId"] = TabIdSchema(),
            ["allTabs"] = new JsonObject { ["type"] = "boolean" },
            ["limit"] = new JsonObject { ["type"] = "integer" },
            ["offset"] = new JsonObject { ["type"] = "integer" }
        };
        if (keywords)
            properties["keywords"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } };
        if (urlKeywords)
        {
            properties["urlKeywords"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } };
            properties["bodyKeywords"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } };
        }

        return new JsonObject { ["type"] = "object", ["properties"] = properties };
    }
}
