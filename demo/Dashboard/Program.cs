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

// spec §10: 数据端点异常统一返回 503;客户端断连(OCE)交回框架,勿伪装 503
async Task<IResult> Db<T>(Func<Task<T>> query)
{
    try { return Results.Ok(await query()); }
    catch (OperationCanceledException) { throw; } // 客户端断连:不返 503,交回 ASP.NET Core 处理
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Dashboard 数据端点查询失败,返回 503");
        return Results.Json(new { error = "database-unavailable" }, statusCode: 503);
    }
}

// 数据端点(均需登录)
app.MapGet("/api/sessions", (ChatHistoryQueries q, CancellationToken ct) =>
    Db(() => q.ListSessionsAsync(ct))).RequireAuthorization();

app.MapGet("/api/sessions/{sessionId}/messages", (string sessionId, ChatHistoryQueries q, CancellationToken ct) =>
    Db(() => q.GetSessionMessagesAsync(sessionId, ct))).RequireAuthorization();

app.MapGet("/api/users", (ChatHistoryQueries q, CancellationToken ct) =>
    Db(() => q.ListUsersAsync(ct))).RequireAuthorization();

app.MapGet("/api/search", (string? q, ChatHistoryQueries queries, CancellationToken ct) =>
    Db(() => queries.SearchAsync(q ?? "", ct))).RequireAuthorization();

app.MapFallbackToFile("index.html");

app.Run();

public sealed record LoginRequest(string Password);
