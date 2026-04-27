// Load .env from executable directory

using System.Text.Json.Serialization;
using FeishuAdaptor;
using ManInBlack.AI.Configuration;
using Microsoft.Extensions.Http;
using ManInBlack.AI;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;

// 从 ~/.man-in-black/settings.json 读取配置
var settings = SettingsLoader.Load();
var feishu = settings.Feishu
    ?? throw new InvalidOperationException("settings.json 中缺少 feishu 配置节。");

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFeishuNetSdk(
    options =>
    {
        options.AppId = feishu.AppId;
        options.AppSecret = feishu.AppSecret;
        options.VerificationToken = feishu.VerificationToken;
        options.EnableLogging = true;
        options.IgnoreStatusException = false;
    },
    opts =>
    {
        opts.HttpHost = new Uri(feishu.ApiBaseUrl);
        opts.JsonSerializeOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        opts.KeyValueSerializeOptions.IgnoreNullValues = true;
    }
)
    .AddFeishuWebSocket()
    // 👆 un comment this line to enable WebSocket connection for receiving real-time events from Feishu, which is more efficient than HTTP polling.
    // Make sure to configure the WebSocket endpoint and authentication in FeishuNetSdk options if you enable this.
;

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

builder.Services.AddManInBlackFromSettings();

builder.Services.AddAutoRegisteredServices();

var app = builder.Build();

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
app.UseFeishuEndpoint("/feishu/event/v2");

app.Run();
