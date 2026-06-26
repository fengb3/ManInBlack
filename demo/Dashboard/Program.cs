using System.Security.Claims;
using ManInBlack.AI.Abstraction.Storage;
using ManInBlack.AI.Configuration;
using ManInBlack.AI.Persistence;
using ManInBlack.Dashboard.Auth;
using ManInBlack.Dashboard.Data;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// 1) 配置源:复用 ~/.man-in-black/settings.json
builder.Configuration.AddManInBlackSettings();

// 2) 绑定 Storage 节 + Dashboard 节
builder.Services.Configure<AgentStorageOptions>(builder.Configuration.GetSection("Storage"));
builder.Services.Configure<DashboardOptions>(builder.Configuration.GetSection("Dashboard"));

// 3) 自注册只读 DbContextFactory(不调用 AddManInBlack,不跑迁移)
builder.Services.AddDbContextFactory<ManInBlackDbContext>((sp, o) =>
{
    var root = sp.GetRequiredService<IOptions<AgentStorageOptions>>().Value.RootPath;
    o.UseSqlite($"Data Source={Path.Combine(root, "maninblack.db")};Mode=ReadOnly");
});

builder.Services.AddSingleton<ChatHistoryQueries>();

// 4) API JSON:camelCase + 枚举字符串(camelCase),与前端 TS 类型对齐
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});

// 5) Cookie 鉴权:API 返回 401 而非 302 跳转(适配 SPA fetch)
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.ExpireTimeSpan = TimeSpan.FromHours(12);
        o.Events.OnRedirectToLogin = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

// Fail-closed:无密码拒启动
AuthService.EnsureConfigured(app.Services.GetRequiredService<IOptions<DashboardOptions>>().Value);

app.UseAuthentication();
app.UseAuthorization();

// 静态文件 + SPA 回退(wwwroot 由 Vite 构建产物填充)
app.UseDefaultFiles();
app.UseStaticFiles();

// 鉴权端点
app.MapGet("/api/me", (HttpContext ctx) =>
    Results.Ok(new { authenticated = ctx.User.Identity?.IsAuthenticated == true }))
    .AllowAnonymous();

app.MapPost("/api/login", async (LoginRequest req, IOptions<DashboardOptions> opts, HttpContext ctx) =>
{
    if (!AuthService.VerifyPassword(opts.Value.Password, req.Password))
        return Results.Unauthorized();
    var identity = new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme);
    await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    return Results.Ok(new { authenticated = true });
}).AllowAnonymous();

app.MapPost("/api/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Ok();
}).RequireAuthorization();

// 数据端点(均需登录)
app.MapGet("/api/sessions", async (ChatHistoryQueries q, CancellationToken ct) =>
    Results.Ok(await q.ListSessionsAsync(ct))).RequireAuthorization();

app.MapGet("/api/sessions/{sessionId}/messages", async (string sessionId, ChatHistoryQueries q, CancellationToken ct) =>
    Results.Ok(await q.GetSessionMessagesAsync(sessionId, ct))).RequireAuthorization();

app.MapGet("/api/users", async (ChatHistoryQueries q, CancellationToken ct) =>
    Results.Ok(await q.ListUsersAsync(ct))).RequireAuthorization();

app.MapGet("/api/search", async (string? q, ChatHistoryQueries queries, CancellationToken ct) =>
    Results.Ok(await queries.SearchAsync(q ?? "", ct))).RequireAuthorization();

app.MapFallbackToFile("index.html");

app.Run();

public sealed record LoginRequest(string Password);
