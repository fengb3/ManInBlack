// Load .env from executable directory

using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using FeishuAdaptor;
using FeishuAdaptor.FeishuCard;
using FeishuNetSdk;
using FeishuAdaptor.FeishuCard.Cards;
using FeishuAdaptor.FeishuCard.CardViews;
using ManInBlack.AI;
using ManInBlack.AI.Core;
using ManInBlack.AI.Core.Attributes;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;

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
        options.VerificationToken = "2coDbVG2uErFAayFpYu5GfBaKTIKVGc3";
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
).AddFeishuWebSocket();

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
        .WriteTo.Console(theme: AnsiConsoleTheme.Code);
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

app.MapGet("/health", () =>
{
    // returns random health status for demonstration purposes
    string[] healthyTexts = ["feeling great!", "ready to serve!", "fully operational!"];
    var random = new Random();
    var text = healthyTexts[random.Next(healthyTexts.Length)];
    return Results.Ok(new { status = "healthy", message = text });
});

app.UseFeishuEndpoint("/feishu/event/v2");

app.MapGet("/test-card", async (IServiceProvider sp, ILogger<Program> logger) =>
{
    var scope = sp.CreateScope();
    var view = scope.ServiceProvider.GetRequiredService<MyCardView>();

    var vm = view.ViewModel;
    var sc = view.StreamingCard;

    await view.InitializeAsync();
    await view.SendToUserAsync("open_id", "ou_a1eef73df84a8a400da4010289610e2f");

    // 1 秒后开始 100 次快速更新
    _ = Task.Run(async () =>
    {
        await Task.Delay(1000);
        for (var i = 1; i <= 100; i++)
        {
            vm.Name += $" {i}";
            vm.Content = $"Counter: {i}/100 — {DateTime.Now:HH:mm:ss.fff}";
            await Task.Delay(50);
        }

        // 等待调度器刷新完最后一批脏元素
        await Task.Delay(200);
    }).ContinueWith(t =>
    {
        if (t.IsFaulted)
        {
            logger.LogError(t.Exception, "Error during card updates");
        }

        scope.Dispose();
    });

    return Results.Ok(new { cardId = sc.CardId, message = "Card sent, 100 updates in 1s" });
});

app.Run();


// ──────────── ViewModel ────────────

[ServiceRegister.Transient]
public partial class MyViewModel : ViewModelBase
{
    [ObservableProperty] public partial string Name { get; set; } = "Initial Name";

    [ObservableProperty] public partial string Content { get; set; } = "Initial Content";
}

// ──────────── CardView ────────────

[ServiceRegister.Transient]
public class MyCardView(MyViewModel viewModel, StreamingCard sc) : CardView<MyViewModel>(viewModel, sc)
{
    protected override void Define()
    {
        AddToBody(
            BindMarkdown(vm => vm.Name),
            Hr(),
            BindMarkdown(vm => vm.Content)
        );
    }
}