using GitHubAdaptor;
using GitHubAdaptor.Models;
using GitHubAdaptor.Webhook;
using ManInBlack.AI;
using ManInBlack.AI.Configuration;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddManInBlackSettings();

var githubSettings = new GitHubSettings();
builder.Configuration.GetSection("GitHub").Bind(githubSettings);

builder.Services.AddSerilog(loggerConfig => loggerConfig.ReadFrom.Configuration(builder.Configuration));
builder.Services.AddHttpClient();
builder.Services.AddSingleton(githubSettings);
builder.Services.AddManInBlack()
    .UseConfiguration(builder.Configuration)
    .AddPipeline("github", pipeline => pipeline.UseDefault());
builder.Services.AddAutoRegisteredServices();

var app = builder.Build();

app.UseMiddleware<GitHubWebhookMiddleware>();

app.MapPost(githubSettings.WebhookEndpoint, async (
    HttpContext context,
    GitHubEventDispatcher dispatcher) =>
{
    var body = (string)context.Items["RawBody"]!;
    var eventType = context.Request.Headers["X-GitHub-Event"].FirstOrDefault() ?? "";

    _ = Task.Run(async () =>
    {
        try
        {
            await dispatcher.DispatchAsync(eventType, body);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "处理 GitHub 事件失败，event: {EventType}", eventType);
        }
    });

    return Results.Ok();
});

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();
