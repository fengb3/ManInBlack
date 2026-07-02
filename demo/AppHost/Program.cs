// Aspire AppHost:一条命令同时启动 FeishuAdaptor(飞书 bot)、Dashboard API、Dashboard 前端(Vite)。
// 数据流不变 —— 飞书写 ~/.man-in-black/maninblack.db,Dashboard 只读同一 DB。

using Aspire.Hosting.Docker.Resources;
using Aspire.Hosting.Docker.Resources.ComposeNodes;
using Aspire.Hosting.Docker.Resources.ServiceNodes;

var builder = DistributedApplication.CreateBuilder(args);

// 生产(发布/compose)目标:Docker Compose publisher。
// 所有容器级配置(user/HOME/端口/卷/cap_add/healthcheck)都在 ConfigureComposeFile 里写进代码,
// `aspire do prepare-prod` 产出的 docker-compose.yaml 直接生产可用,无需再手改。
// 镜像构建:各项目经 PublishAsDockerFile(WithDockerfile) 指向自己的 Dockerfile,
// prepare-prod 会用这些 Dockerfile build 出镜像(bwrap/node/python 等运行时依赖都在里面)。
builder.AddDockerComposeEnvironment("prod")
    .WithDashboard(enabled: false)              // 不带 Aspire 遥测面板,只跑 feishu + dashboard 两个业务容器
    .ConfigureComposeFile(ConfigureProdCompose);

// 飞书 bot:沿用其 launchSettings 的 http profile(:5249);/health 由 ServiceDefaults 提供(生产也映射)。
// 生产镜像用 demo/FeishuAdaptor/Dockerfile(装了 node/python/bwrap/curl),镜像内 ASPNETCORE_URLS=8080。
// 镜像 tag 用 Aspire 默认的 <资源>:<sha>(无 registry 时无法改成版本号——已实测多种 API 都不行),
// build-prod.sh 会读实际 sha 对齐 .env。
var feishu = builder.AddProject<Projects.FeishuAdaptor>("feishu")
    .WithHttpHealthCheck("/health")
    .PublishAsDockerFile(c => c.WithDockerfile("../..", "demo/FeishuAdaptor/Dockerfile", null));

// Dashboard API:沿用其 launchSettings(:5080)。
// 生产镜像用 demo/Dashboard/Dockerfile(node 阶段构建前端),镜像内 ASPNETCORE_URLS=5080。
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

// 把原"手调 compose"的字段全部在代码里固化到生成的 compose 上。
// Service 模型是完整 compose schema:CapAdd/SecurityOpt/User/Volumes/Ports/Healthcheck/Environment 都有。
// 详见 docs/aspire-guide.md「生产部署」与 docs/migration-systemd-to-aspire.md。
void ConfigureProdCompose(ComposeFile composeFile)
{
    // 飞书:容器内 bubblewrap 需特权;数据卷挂到 ~/.man-in-black;监听 8080(镜像内 ASPNETCORE_URLS)。
    var feishu = composeFile.Services["feishu"];
    feishu.User = "0:0";
    feishu.Restart = "unless-stopped";
    feishu.CapAdd = ["SYS_ADMIN"];                   // bwrap 容器内创建 namespace 所需
    feishu.SecurityOpt = ["apparmor=unconfined"];    // 配合 SYS_ADMIN,绕过 apparmor 限制
    feishu.Environment["HOME"] = "/root";            // 否则 ~/.man-in-black 解析成数据路径下嵌套
    feishu.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
    feishu.Environment.Remove("HTTP_PORTS");         // 让镜像内 ASPNETCORE_URLS=8080 唯一生效,避免双配置
    feishu.Environment.Remove("OTEL_EXPORTER_OTLP_ENDPOINT"); // 关掉遥测面板后,别往不存在的 endpoint 发
    feishu.Volumes = [new Volume { Name = "/root/.man-in-black", Source = "/root/.man-in-black", Target = "/root/.man-in-black", Type = "bind" }];
    feishu.Healthcheck = new Healthcheck
    {
        Test = ["CMD-SHELL", "curl -fsS http://localhost:8080/health || exit 1"],
        Interval = "30s",
        Timeout = "5s",
        StartPeriod = "20s",
    };

    // Dashboard:无 bwrap;端口仅绑 loopback(公网不可达,经 SSH 隧道访问);监听 5080。
    var dashboard = composeFile.Services["dashboard"];
    dashboard.User = "0:0";
    dashboard.Restart = "unless-stopped";
    dashboard.Environment["HOME"] = "/root";
    dashboard.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
    dashboard.Environment.Remove("HTTP_PORTS");
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
