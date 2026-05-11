// Load .env from executable directory

using System.Text.Json.Serialization;
using FeishuAdaptor;
using ManInBlack.AI;
using ManInBlack.AI.Configuration;
using ManInBlack.AI.Middlewares;
using Microsoft.Extensions.Http;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;

var builder = WebApplication.CreateBuilder(args);

// 将 ManInBlack 配置源添加到 Host Configuration（启用 reloadOnChange）
builder.Configuration.AddManInBlackSettings();

// 从统一 IConfiguration 读取飞书配置
var feishuSettings = new FeishuSettings();
builder.Configuration.GetSection("Feishu").Bind(feishuSettings);
if (string.IsNullOrEmpty(feishuSettings.AppId))
    throw new InvalidOperationException("settings.json 中缺少 feishu 配置节。");

var feishuBuilder = builder.Services.AddFeishuNetSdk(
    options =>
    {
        options.AppId = feishuSettings.AppId;
        options.AppSecret = feishuSettings.AppSecret;
        options.VerificationToken = feishuSettings.VerificationToken;
        options.EnableLogging = true;
        options.IgnoreStatusException = false;
    },
    opts =>
    {
        opts.HttpHost = new Uri(feishuSettings.ApiBaseUrl);
        opts.JsonSerializeOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        opts.KeyValueSerializeOptions.IgnoreNullValues = true;
    }
);

if (feishuSettings.EnableWebSocket)
    feishuBuilder.AddFeishuWebSocket();

builder.Services.AddSerilog(loggerConfig =>
{
    // Suppress verbose "sending request" / HTTP traffic logs by raising
    // the minimum level for common noisy namespaces to Warning.
    // Add more namespaces here if you still see outgoing-request log lines.
    loggerConfig
        .MinimumLevel.Override("System.Net.Http", LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.Extensions.Http", LogEventLevel.Warning)
        .MinimumLevel.Override("FeishuNetSdk", LogEventLevel.Warning)
        .MinimumLevel.Override("OpenAI", LogEventLevel.Warning)
        .WriteTo.Console(theme: AnsiConsoleTheme.Code)
        .WriteTo.File(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".man-in-black", "logs", "log-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 7
        );
});

builder.Services.AddManInBlackFromConfiguration(builder.Configuration);

// Agent 定义现在从 settings.json 的 Agents 字段自动加载，无需 AddAgentDefinition 调用
// Pipeline 注册仍在代码中（涉及中间件类型）

builder.Services.AddAutoRegisteredServices();

var app = builder.Build();

// 注册飞书自定义管道（需在 Build 后获取 Factory 实例）
var factory = app.Services.GetRequiredService<AgentFactory>();
factory.RegisterPipeline("feishu", pipeline => pipeline.UseDefault());

// 子 Agent 专用 pipeline：有文件工具和事件发布，无 DelegationMiddleware
factory.RegisterPipeline("sub-agent", builder => builder
    .Use<EventPublishingMiddleware>()
    .Use<FileToolsMiddleware>()
    .UseSimple());

// 愿你健康, 开心, 美满, 幸福
app.MapGet(
    "/health",
    () =>
    {
        // returns random health status for demonstration purposes
        string[] healthyTexts = ["feeling great!", "ready to serve!", "fully operational!"];
        var random = new Random();
        var text = healthyTexts[random.Next(healthyTexts.Length)];
        return Results.Ok(new { status = "healthy", message = text });
    }
);

// Map Feishu event endpoint, and the FeishuAdaptor will handle incoming events according to the registered handlers
if (!string.IsNullOrEmpty(feishuSettings.WebhookEndpoint))
    app.UseFeishuEndpoint(feishuSettings.WebhookEndpoint);

app.Run();
