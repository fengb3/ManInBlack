// Load .env from executable directory

using System.Text.Json.Serialization;
using FeishuAdaptor;
using Microsoft.Extensions.Http;
using ManInBlack.AI;
using ManInBlack.AI.Core;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;

// read comfiguration form the fucking .env file, which should be placed in the same directory as the executable, and contains the following variables:
var envPath = Path.Combine(AppContext.BaseDirectory, ".env");
if (File.Exists(envPath))
    Env.Load(envPath);
else
    Env.Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFeishuNetSdk(
    options =>
    {
        options.AppId =
            Environment.GetEnvironmentVariable("FEISHU_APP_ID")
            ?? throw new InvalidOperationException(
                "FEISHU_APP_ID environment variable is not set."
            );
        options.AppSecret =
            Environment.GetEnvironmentVariable("FEISHU_APP_SECRET")
            ?? throw new InvalidOperationException(
                "FEISHU_APP_SECRET environment variable is not set."
            );
        options.VerificationToken = 
            Environment.GetEnvironmentVariable("FEISHU_APP_VERIFICATION_TOKEN")
            ?? throw new InvalidOperationException(
                "FEISHU_APP_SECRET environment variable is not set."
            );
        options.EnableLogging = true;
        options.IgnoreStatusException = false;
    },
    opts =>
    {
        opts.HttpHost = new Uri(
            Environment.GetEnvironmentVariable("FEISHU_API_BASE_URL") ?? "https://open.feishu.cn/"
        );
        opts.JsonSerializeOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        opts.KeyValueSerializeOptions.IgnoreNullValues = true;
    }
)
    // .AddFeishuWebSocket()
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

builder.Services.AddManInBlack(opt =>
{
    opt.ModelChoice = new ModelChoice
    {
        Provider = new OpenAIProvider()
        {
            ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "",
            BaseUrl = Environment.GetEnvironmentVariable("OPENAI_BASE_URL") ?? "",
        },
        ModelId = Environment.GetEnvironmentVariable("OPENAI_MODEL_ID") ?? "",
    };
});

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
