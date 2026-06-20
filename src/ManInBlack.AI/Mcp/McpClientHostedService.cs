using ManInBlack.AI.Configuration;
using ManInBlack.AI.Tools;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace ManInBlack.AI.Mcp;

/// <summary>
/// 应用启动时连接所有配置的 MCP server，列举其工具并注册到 ToolRegistry（让模型可见）。
/// 单个 server 连接失败只记日志并加入重试队列，不阻断应用启动。Singleton 语义。
/// </summary>
public sealed class McpClientHostedService(
    IOptions<ManInBlackSettings> settings,
    ToolRegistry toolRegistry,
    ILoggerFactory loggerFactory,
    ILogger<McpClientHostedService> logger) : IHostedService, IAsyncDisposable
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(30);

    private readonly Dictionary<string, McpClient> _clients = new();
    private readonly Dictionary<string, McpToolDescriptor> _toolsByFqn = new();
    private readonly HashSet<string> _failedServers = new();
    private Task? _startTask;
    private readonly object _startLock = new();
    private DateTime _lastConnectAttempt = DateTime.MinValue;

    /// <summary>已连接的 MCP server 名 → client。</summary>
    public IReadOnlyDictionary<string, McpClient> Clients => _clients;

    /// <summary>所有已连接 server 暴露的工具：完全限定名 → 描述符（O(1) 查找）。</summary>
    public IReadOnlyDictionary<string, McpToolDescriptor> ToolsByFqn => _toolsByFqn;

    public Task StartAsync(CancellationToken cancellationToken) => EnsureStartedAsync(cancellationToken);

    /// <summary>
    /// 确保 MCP server 已连接（并发安全 + 失败可重连）：首个调用启动连接，后续调用 await 同一个 Task，
    /// 避免“已标记启动但连接未完成”的竞态；若存在启动期失败的 server 且过重试间隔，则重连它们。
    /// </summary>
    public Task EnsureStartedAsync(CancellationToken cancellationToken = default)
    {
        if (_startTask is not null)
        {
            // 已启动过：全部成功直接返回；若有失败 server 且过重试间隔，重连
            if (_startTask.IsCompleted && _failedServers.Count > 0
                && DateTime.UtcNow - _lastConnectAttempt > RetryInterval)
            {
                return ReconnectFailedAsync();
            }
            return _startTask;
        }
        lock (_startLock)
        {
            _startTask ??= ConnectAllAsync();
            return _startTask;
        }
    }

    private async Task ConnectAllAsync()
    {
        _lastConnectAttempt = DateTime.UtcNow;
        foreach (var (name, cfg) in settings.Value.McpServers)
        {
            if (!cfg.Enabled) continue;
            await ConnectOneAsync(name, cfg);
        }
    }

    private async Task ReconnectFailedAsync()
    {
        _lastConnectAttempt = DateTime.UtcNow;
        foreach (var name in _failedServers.ToArray())
        {
            if (!settings.Value.McpServers.TryGetValue(name, out var cfg) || !cfg.Enabled)
            {
                _failedServers.Remove(name);
                continue;
            }
            // 成功则 ConnectOneAsync 内部移除 _failedServers；失败则保持，等下次重试
            await ConnectOneAsync(name, cfg);
        }
    }

    /// <summary>
    /// 连接单个 MCP server 并注册其工具。失败则记入 _failedServers（供后续重连），不抛异常。
    /// </summary>
    private async Task ConnectOneAsync(string name, McpServerSettings cfg)
    {
        try
        {
            var transport = BuildTransport(name, cfg);
            var options = new McpClientOptions
            {
                ClientInfo = new Implementation { Name = "ManInBlack", Version = "1.0" },
                InitializationTimeout = TimeSpan.FromSeconds(cfg.ConnectionTimeoutSeconds),
            };
            // 连接超时由 InitializationTimeout/transport 配置控制，不随某次调用方的 ct 取消
            // （避免首个请求取消导致连接中断、后续永久不可用）
            var client = await McpClient.CreateAsync(transport, options, loggerFactory, CancellationToken.None);
            _clients[name] = client;

            var tools = await client.ListToolsAsync((ModelContextProtocol.RequestOptions?)null, CancellationToken.None);
            foreach (var tool in tools)
            {
                var fqn = $"{name}__{tool.Name}";
                var descriptor = new McpToolDescriptor(name, tool.Name, fqn, tool.WithName(fqn));
                _toolsByFqn[fqn] = descriptor;
                toolRegistry.Register(new ToolDeclaration(fqn, "mcp", descriptor.Tool));
                logger.LogInformation("注册 MCP 工具 {Fqn}", fqn);
            }
            _failedServers.Remove(name);
            logger.LogInformation("MCP server {Name} 已连接，提供 {Count} 个工具", name, tools.Count);
        }
        catch (Exception ex)
        {
            _failedServers.Add(name);
            logger.LogError(ex, "MCP server {Name} 连接失败，已加入重试队列", name);
        }
    }

    private IClientTransport BuildTransport(string name, McpServerSettings cfg)
    {
        var isHttp = cfg.Transport.Equals("http", StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrEmpty(cfg.Endpoint) && string.IsNullOrEmpty(cfg.Command));

        if (isHttp)
        {
            if (string.IsNullOrEmpty(cfg.Endpoint))
                throw new InvalidOperationException($"MCP server '{name}' 配置为 http 但缺少 Endpoint");

            var httpOpts = new HttpClientTransportOptions
            {
                Endpoint = new Uri(cfg.Endpoint),
                Name = name,
                ConnectionTimeout = TimeSpan.FromSeconds(cfg.ConnectionTimeoutSeconds),
            };
            if (cfg.Headers is { Count: > 0 })
                httpOpts.AdditionalHeaders = new Dictionary<string, string>(cfg.Headers);
            if (Enum.TryParse(cfg.TransportMode, ignoreCase: true, out HttpTransportMode mode))
                httpOpts.TransportMode = mode;
            return new HttpClientTransport(httpOpts, loggerFactory);
        }

        if (string.IsNullOrEmpty(cfg.Command))
            throw new InvalidOperationException($"MCP server '{name}' 配置为 stdio 但缺少 Command");

        var stdioOpts = new StdioClientTransportOptions
        {
            Command = cfg.Command,
            Name = name,
            ShutdownTimeout = TimeSpan.FromSeconds(cfg.ShutdownTimeoutSeconds),
        };
        if (cfg.Arguments is { Count: > 0 })
            stdioOpts.Arguments = new List<string>(cfg.Arguments);
        if (!string.IsNullOrEmpty(cfg.WorkingDirectory))
            stdioOpts.WorkingDirectory = cfg.WorkingDirectory;
        if (cfg.Environment is { Count: > 0 })
        {
            var env = new Dictionary<string, string?>();
            foreach (var e in cfg.Environment)
                if (e.Value is not null)
                    env[e.Key] = e.Value;
            stdioOpts.EnvironmentVariables = env;
        }
        stdioOpts.StandardErrorLines = line => logger.LogWarning("MCP {Name} stderr: {Line}", name, line);
        return new StdioClientTransport(stdioOpts, loggerFactory);
    }

    public Task StopAsync(CancellationToken cancellationToken) => DisposeAsync().AsTask();

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients.Values)
        {
            try { await client.DisposeAsync(); }
            catch (Exception ex) { logger.LogWarning(ex, "MCP client dispose 失败"); }
        }
        _clients.Clear();
        _toolsByFqn.Clear();
        _failedServers.Clear();
    }
}
