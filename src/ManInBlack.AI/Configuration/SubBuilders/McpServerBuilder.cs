namespace ManInBlack.AI.Configuration;

/// <summary>
/// 流式构建 McpServerSettings。
/// </summary>
public sealed class McpServerBuilder
{
    internal McpServerSettings Settings { get; } = new();

    /// <summary>设置传输协议："stdio" 或 "http"。</summary>
    public McpServerBuilder Transport(string transport) { Settings.Transport = transport; return this; }

    /// <summary>设置 stdio 模式下的可执行命令。</summary>
    public McpServerBuilder Command(string command) { Settings.Command = command; return this; }

    /// <summary>设置 stdio 模式下的命令参数。</summary>
    public McpServerBuilder Arguments(params string[] args) { Settings.Arguments = [..args]; return this; }

    /// <summary>设置 stdio 模式下的子进程工作目录。</summary>
    public McpServerBuilder WorkingDirectory(string dir) { Settings.WorkingDirectory = dir; return this; }

    /// <summary>添加 stdio 模式下的子进程环境变量。</summary>
    public McpServerBuilder Environment(string key, string? value)
    {
        Settings.Environment ??= new Dictionary<string, string?>();
        Settings.Environment[key] = value;
        return this;
    }

    /// <summary>设置 http 模式下的 MCP server 端点。</summary>
    public McpServerBuilder Endpoint(string endpoint) { Settings.Endpoint = endpoint; return this; }

    /// <summary>添加 http 模式下的额外请求头。</summary>
    public McpServerBuilder Header(string key, string value)
    {
        Settings.Headers ??= new Dictionary<string, string>();
        Settings.Headers[key] = value;
        return this;
    }

    /// <summary>设置 http 传输模式："AutoDetect"|"Sse"|"StreamableHttp"。</summary>
    public McpServerBuilder TransportMode(string mode) { Settings.TransportMode = mode; return this; }

    /// <summary>设置连接/初始化超时（秒）。</summary>
    public McpServerBuilder ConnectionTimeoutSeconds(int seconds) { Settings.ConnectionTimeoutSeconds = seconds; return this; }

    /// <summary>设置 stdio 子进程关闭超时（秒）。</summary>
    public McpServerBuilder ShutdownTimeoutSeconds(int seconds) { Settings.ShutdownTimeoutSeconds = seconds; return this; }

    /// <summary>设置是否启用此 server。</summary>
    public McpServerBuilder Enabled(bool enabled) { Settings.Enabled = enabled; return this; }
}
