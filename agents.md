# AGENTS.md

BrowserTools 的 C# 移植写在本仓库。Chrome 扩展保持 JS；服务端必须 Native AOT。

## 工程拆分

**除 UI 外的新增代码必须放进独立类库，不得写入 `FlameshotClipboardHelper.csproj`。**

| 项目 | 类型 | 放什么 |
| --- | --- | --- |
| `BrowserTools/`（类库） | `Microsoft.NET.Sdk` classlib | 除 UI 外的全部新代码 |
| `BrowserTools.Host/` | console exe | 仅 `Main`、CLI 解析、进程生命周期；引用类库 |
| `FlameshotClipboardHelper` | 现有 WinExe | 剪贴板托盘；禁止塞 BrowserTools |
| `chrome-extension/` | JS | 浏览器 UI；只读，协议冻结 |
| `Ui/` | 现有 Avalonia | 仅剪贴板助手界面 |

类库内部两个区域（仍在同一个 `.csproj` 里）：

| 路径 | 放什么 | 禁止 |
| --- | --- | --- |
| `BrowserTools/Core/` | store、settings 白名单、脱敏、截断、HAR、会话/token、路径沙箱、安全谓词 | HTTP、WebSocket、MCP JSON-RPC、Avalonia、进程入口 |
| `BrowserTools/Mcp/` | 连接器（loopback HTTP + `/extension-ws`）、MCP stdio、doctor、`ConnectorClient` | 查询/脱敏/截断等业务规则（必须调 `Core/`） |

```
BrowserTools/              独立类库（AOT）
  Core/
  Mcp/
BrowserTools.Host/         薄入口 exe，引用上面的类库
chrome-extension/          只读
FlameshotClipboardHelper   现有托盘，不动
```

命名空间：`BrowserTools`（Core）、`BrowserTools.Mcp`。现有类型保持 `FlameshotClipboardHelper.Core`。

Host 不得实现业务或协议细节；新类型、新文件一律进类库。不要把 MCP 并进 `FlameshotClipboardHelper.exe`。

## 产品边界

- 扩展不改。对齐 2.0：`signature = mcp-browser-connector-24x7`，端口 `3025–3035`，WS type（`hello` / `console` / `welcome` / `capture-screenshot` 等），session 文件 `%USERPROFILE%\.browser-tools-mcp\session.json`。
- 采集、截图压缩、页面脚本、点击输入只在扩展里；C# 只做请求-响应转发。
- Lighthouse 不进进程。第一期可关掉 audit 工具；需要时再外挂，且不得破坏 AOT。

## Native AOT（硬性）

类库与 Host 都要：

- `IsAotCompatible=true`、`PublishAot=true`、`IsTrimmable=true`
- `TreatWarningsAsErrors` 覆盖 `IL2026`、`IL3050`、`IL2104`
- 发布：`dotnet publish BrowserTools.Host -c Release -r win-x64 -p:PublishAot=true`

禁止：

- 未标注 `IsAotCompatible` 的 NuGet（默认禁止官方 MCP SDK，除非核实可 trim/AOT）
- Newtonsoft.Json、`dynamic`、运行时代码生成、约定控制器/反射路由
- 无 `JsonSerializerContext` 的 `System.Text.Json` 序列化
- 无说明地写 `RequiresUnreferencedCode` / `UnconditionalSuppressMessage`

必须：

- 全部 DTO 进 source-generated `JsonSerializerContext`（`[JsonSerializable]`）
- MCP JSON-RPC 手写 stdio；stdout 只走 RPC，日志只写 stderr
- HTTP/WS 用 `HttpListener` + `AcceptWebSocketAsync`，或 ASP.NET Native AOT + `EnableRequestDelegateGenerator`。优先 HttpListener（体积）
- 连接器只绑 `127.0.0.1`；校验 `Host`；WS 只接受扩展 Origin，否则要 token

## Core vs Mcp

- **Core**：按 tab 归属（不信消息体 `tabId`）、写入时脱敏、字符预算从最新往回截、settings allowlist + clamp、截图文件名/目录沙箱、HAR。用原仓库单测当对照。
- **Mcp**：identity、upgrade、`requestId` 绑连接、心跳、runtime 查找顺序（`--connect` → session → 嵌入 → 降级）、工具名与 2.0 对齐（`getConsoleLogs`、`takeScreenshot`…）、超大结果走 resource，不 inline。

## 验收

1. `dotnet publish` AOT 成功，无 IL trim 警告。
2. 现有扩展零修改，F12 能连上 C# 连接器。
3. Cursor 能调非 audit 工具；未开 DevTools 时返回可读错误，而不是握手失败。
4. 非 loopback / 错误 Origin / 无 token → 401/403。
