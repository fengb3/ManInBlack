// Load .env from executable directory

using System.Text.Json.Serialization;
using FeishuAdaptor;
using ManInBlack.AI;
using ManInBlack.AI.Configuration;
using ManInBlack.AI.Middlewares;
using ManInBlack.AI.Persistence;
using Microsoft.Extensions.Http;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

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

// Agent 定义现在从 settings.json 的 Agents 字段自动加载，无需 AddAgentDefinition 调用

builder.Services.AddManInBlack()
    .UseConfiguration(builder.Configuration)
    .AddFeishu(f => builder.Configuration.GetSection("Feishu").Bind(f))
    .AddPipeline("feishu", pipeline => pipeline.UseDefault(b => b.Use(
        new ToolIntentSchemaMiddleware("purpose", "用一句话讲述你调用这个工具是为了做什么。", required: true))))
    .AddPipeline("sub-agent", builder => builder
        .Use<EventPublishingMiddleware>()
        .Use<ToolsMiddleware>()
        .Use(new ToolIntentSchemaMiddleware("purpose", "用一句话讲述你调用这个工具是为了做什么。", required: true))
        .UseSimple());

builder.Services.AddAutoRegisteredServices();

var app = builder.Build();

// 一次性 JSON→SQLite 迁移子命令(执行后退出,不启动 Web 服务/不连飞书)
if (args.Contains("migrate-storage"))
{
    await app.Services.MigrateManInBlackStorageAsync();
    var migrator = app.Services.GetRequiredService<JsonToSqliteMigrator>();
    var summary = await migrator.MigrateAsync();
    Console.WriteLine($"迁移完成:消息 {summary.Messages},快照 {summary.Snapshots},用户 {summary.Users},跳过 {summary.Skipped}");
    return;
}

// 启动期应用 EF Core 迁移(已最新则空操作)
await app.Services.MigrateManInBlackStorageAsync();

// /health、/alive 端点由 ServiceDefaults 提供(仅 Development)。Aspire 据此做健康探测。
app.MapDefaultEndpoints();

// Map Feishu event endpoint, and the FeishuAdaptor will handle incoming events according to the registered handlers
if (!string.IsNullOrEmpty(feishuSettings.WebhookEndpoint))
    app.UseFeishuEndpoint(feishuSettings.WebhookEndpoint);

app.Run();
