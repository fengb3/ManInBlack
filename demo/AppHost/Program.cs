// Aspire AppHost:一条命令同时启动 FeishuAdaptor(飞书 bot)、Dashboard API、Dashboard 前端(Vite)。
// 数据流不变 —— 飞书写 ~/.man-in-black/maninblack.db,Dashboard 只读同一 DB。

var builder = DistributedApplication.CreateBuilder(args);

// 飞书 bot:沿用其 launchSettings 的 http profile(:5249);/health 由 ServiceDefaults 提供。
var feishu = builder.AddProject<Projects.FeishuAdaptor>("feishu")
    .WithHttpHealthCheck("/health");

// Dashboard API:沿用其 launchSettings(:5080)。
var dashboard = builder.AddProject<Projects.Dashboard>("dashboard")
    .WithHttpHealthCheck("/health");

// Dashboard 前端:Vite dev server。AddViteApp 自动注册 http endpoint + PORT 环境变量,
// 禁止再调 WithHttpEndpoint。把后端 URL 经 VITE_API_BASE_URL 注入给 vite.config 的 proxy。
builder.AddViteApp("dashboard-client", "../Dashboard/client")
    .WithReference(dashboard)
    .WithEnvironment("VITE_API_BASE_URL", dashboard.GetEndpoint("http"))
    .ExcludeFromManifest();

builder.Build().Run();
