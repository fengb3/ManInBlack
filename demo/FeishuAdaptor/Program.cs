// Load .env from executable directory
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using FeishuNetSdk;
using FeishuAdaptor.FeishuCard;
using FeishuAdaptor.FeishuCard.Cards;
using FeishuAdaptor.FeishuCard.CardViews;
using ManInBlack.AI;
using ManInBlack.AI.Core;
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
    options => {
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
        options.VerificationToken     = "2coDbVG2uErFAayFpYu5GfBaKTIKVGc3";
        options.EnableLogging         = true;
        options.IgnoreStatusException = false;
    },
    opts => {
        opts.HttpHost = new Uri(
            Environment.GetEnvironmentVariable("FEISHU_API_BASE_URL") ?? "https://open.feishu.cn/"
        );
        opts.JsonSerializeOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        opts.KeyValueSerializeOptions.IgnoreNullValues   = true;
    }
);

builder.Services.AddSerilog(loggerConfig => {
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

builder.Services.AddManInBlack(opt => {
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


var app = builder.Build();

app.MapGet("/health", () => {
    // returns random health status for demonstration purposes
    string[] healthyTexts = ["feeling great!", "ready to serve!", "fully operational!"];
    var      random       = new Random();
    var      text         = healthyTexts[random.Next(healthyTexts.Length)];
    return Results.Ok(new { status = "healthy", message = text });
});

app.UseFeishuEndpoint("/feishu/event/v2");

app.MapGet("/test-card", async (IServiceProvider sp, ILogger<Program> logger) =>
{
    var scope = sp.CreateScope();
    var api = scope.ServiceProvider.GetRequiredService<IFeishuTenantApi>();
    var vm = new MyViewModel();
    var streamingCard = new StreamingCard(api);
    var view = new MyCardView(vm, streamingCard);

    await view.InitAsync();
    await view.SendMessageAsync("open_id", "ou_a8f8946b2bf14d9900e0446bad74f995");

    // 1 秒后开始 100 次快速更新
    _ = Task.Run(async () =>
    {
        await Task.Delay(1000);
        for (var i = 1; i <= 100; i++)
        {
            vm.Name = $"Update #{i}";
            vm.Content = $"Counter: {i}/100 — {DateTime.Now:HH:mm:ss.fff}";
            await Task.Delay(50);
        }
        // 等待调度器刷新完最后一批脏元素
        await Task.Delay(200);
        await view.CloseAsync();
    }).ContinueWith( t => {
        if (t.IsFaulted)
        {
            logger.LogError(t.Exception, "Error during card updates");
        }
        scope.Dispose();
    });

    return Results.Ok(new { cardId = view.CardId, message = "Card sent, 100 updates in 1s" });
});

app.Run();


// ──────────── ViewModel ────────────

public partial class MyViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Name { get; set; } = "Initial Name";

    [ObservableProperty]
    public partial string Content { get; set; } = "Initial Content";
}

// ──────────── CardView ────────────

public class MyCardView(MyViewModel vm, StreamingCard card)
    : StreamingCardView<MyViewModel>(vm, card)
{
    protected override void Define()
    {
        Card.Header = new CardHeader
        {
            Title = new TextElement("MVVM Card Test") { Tag = "plain_text" },
            Template = "blue",
        };

        BindText(vm => vm.Name, tag: "lark_md");
        AddHr();
        BindMarkdown(vm => vm.Content);
    }
}
