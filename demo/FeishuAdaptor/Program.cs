// Load .env from executable directory

using System.Text;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using FeishuAdaptor;
using FeishuAdaptor.FeishuCard;
using FeishuAdaptor.FeishuCard.Cards;
using FeishuAdaptor.FeishuCard.CardViews;
using FeishuNetSdk;
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
)
    // .AddFeishuWebSocket()
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

app.UseFeishuEndpoint("/feishu/event/v2");

app.Run();

// ──────────── ViewModel ────────────

[ServiceRegister.Transient]
public class MyViewModel : ViewModelBase
{
    private readonly StringBuilder _contentBuilder = new("Initial Content");

    public string Content
    {
        get => _contentBuilder.ToString();
        set
        {
            _contentBuilder.Clear();
            _contentBuilder.Append(value);
            OnPropertyChanged();
        }
    }

    public void AppendContent(char value)
    {
        _contentBuilder.Append(value);
        OnPropertyChanged(nameof(Content));
    }

    public void AppendContent(string value)
    {
        _contentBuilder.Append(value);
        OnPropertyChanged(nameof(Content));
    }
}

// ──────────── CardView ────────────

[ServiceRegister.Transient]
public class MyCardView(MyViewModel viewModel, CardService sc, CardUpdateScheduler scheduler)
    : CardView<MyViewModel>(viewModel, sc, scheduler)
{
    protected override void Define() =>
        AddToBody(
            // BindMarkdown(vm => vm.Name),
            // Hr(),
            BindMarkdown(vm => vm.Content)
        );
}
