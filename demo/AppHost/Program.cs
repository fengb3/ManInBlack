// Aspire AppHost:开发时一条命令同时启动 FeishuAdaptor(飞书 bot)、Dashboard API、Dashboard 前端(Vite)。
// 数据流不变 —— 飞书写 ~/.man-in-black/maninblack.db,Dashboard 只读同一 DB。

using Aspire.Hosting.Docker.Resources;
using Aspire.Hosting.Docker.Resources.ComposeNodes;
using Aspire.Hosting.Docker.Resources.ServiceNodes;

var builder = DistributedApplication.CreateBuilder(args);

// 生产(发布/compose)目标:Docker Compose publisher——现在只管 **dashboard**。
// feishu 生产不走 docker:它是 Agent host(要在宿主跑命令、访问同机服务),容器化摩擦大,改由
// systemd + DeployApi 部署(见 demo/FeishuAdaptor/deploy.ps1 + 服务器 feishu-adaptor.service)。
// feishu 经 ExcludeFromManifest 从生产 compose 排除;这里只配 dashboard 的容器级配置。
// dashboard 经 PublishAsDockerFile(WithDockerfile) 用 demo/Dashboard/Dockerfile(node 阶段构建前端)。
builder.AddDockerComposeEnvironment("prod")
    .WithDashboard(enabled: false)              // 不带 Aspire 遥测面板,生产 compose 只跑 dashboard(feishu 走 systemd)
    .ConfigureComposeFile(ConfigureProdCompose);

// 飞书 bot:开发时本地 dotnet run(沿用 launchSettings :5249,/health 由 ServiceDefaults 提供)。
// 生产**不走 docker**:feishu 是 Agent host(在宿主跑命令、访问同机服务),容器化摩擦大,改由
// systemd + DeployApi 部署(见 demo/FeishuAdaptor/deploy.ps1 + 服务器 feishu-adaptor.service)。
// ExcludeFromManifest → 生产 compose 不含 feishu、不 build feishu 镜像。
var feishu = builder.AddProject<Projects.FeishuAdaptor>("feishu")
    .WithHttpHealthCheck("/health")
    .ExcludeFromManifest();

// Dashboard API:沿用其 launchSettings(:5080)。
// 生产镜像用 demo/Dashboard/Dockerfile(node 阶段构建前端);监听 5080 由 Aspire 注入 ASPNETCORE_URLS(镜像不设端口)。
var dashboard = builder.AddProject<Projects.Dashboard>("dashboard")
    .WithHttpHealthCheck("/health")
    .PublishAsDockerFile(c => c.WithDockerfile("../..", "demo/Dashboard/Dockerfile", null));

// Dashboard 前端:Vite dev server。AddViteApp 自动注册 http endpoint + PORT 环境变量,
// 禁止再调 WithHttpEndpoint。把后端 URL 经 VITE_API_BASE_URL 注入给 vite.config 的 proxy。
builder.AddViteApp("dashboard-client", "../Dashboard/client")
    .WithReference(dashboard)
    .WithEnvironment("VITE_API_BASE_URL", dashboard.GetEndpoint("http"))
    .ExcludeFromManifest();

builder.Build().Run();

// dashboard 容器级配置在代码里固化到生成的 compose(user/HOME/端口/卷/healthcheck)。
// Service 模型是完整 compose schema:CapAdd/SecurityOpt/User/Volumes/Ports/Healthcheck/Environment 都有。
// 详见 docs/aspire-guide.md「生产部署」与 docs/migration-systemd-to-aspire.md。
// feishu 不在此(走 systemd,见 demo/FeishuAdaptor/deploy.ps1)。
void ConfigureProdCompose(ComposeFile composeFile)
{
    // Dashboard:无 bwrap;端口仅绑 loopback(公网不可达,经 SSH 隧道访问);监听 5080。
    var dashboard = composeFile.Services["dashboard"];
    dashboard.User = "0:0";
    dashboard.Restart = "unless-stopped";
    dashboard.Environment["HOME"] = "/root";
    dashboard.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
    dashboard.Environment["ASPNETCORE_URLS"] = "http://0.0.0.0:5080"; // 端口归 Aspire 控(镜像不设),compose 映射 127.0.0.1:5080:5080
    dashboard.Environment.Remove("HTTP_PORTS");                       // Aspire 默认 HTTP_PORTS=8080 与 5080 冲突,删掉让 ASPNETCORE_URLS 唯一生效
    dashboard.Environment.Remove("OTEL_EXPORTER_OTLP_ENDPOINT");
    dashboard.Volumes = [new Volume { Name = "/root/.man-in-black", Source = "/root/.man-in-black", Target = "/root/.man-in-black", Type = "bind" }];
    dashboard.Ports = ["127.0.0.1:5080:5080"];       // 仅 loopback,公网不可达,经 SSH 隧道访问
    dashboard.Expose = ["5080"];                     // 实际监听 5080(镜像内 ASPNETCORE_URLS),覆盖 Aspire 默认的 8080
    dashboard.Healthcheck = new Healthcheck
    {
        Test = ["CMD-SHELL", "curl -fsS http://localhost:5080/health || exit 1"],
        Interval = "30s",
        Timeout = "5s",
        StartPeriod = "20s",
    };
}
