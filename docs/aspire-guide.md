# Aspire 编排指南

`demo/AppHost` 是一个 [.NET Aspire](https://aspire.dev) AppHost,一条命令同时拉起三个进程,并在 Aspire Dashboard 统一查看日志、健康检查、遥测:

| 资源 | 项目/目录 | 端口 | 说明 |
|------|-----------|------|------|
| `feishu` | `demo/FeishuAdaptor` | 5249 | 飞书 bot,写 `maninblack.db` |
| `dashboard` | `demo/Dashboard` | 5080 | Dashboard API,只读同一 DB |
| `dashboard-client` | `demo/Dashboard/client` | Aspire 动态分配(单独跑为 5173) | Vite 前端,proxy `/api` → dashboard |

## 前置

- .NET 10 SDK
- Aspire CLI(可选,用于 `aspire` 命令):`dotnet tool install --global aspire.cli`
- Dashboard 前端依赖:`cd demo/Dashboard/client && npm install`(首次)
- `~/.man-in-black/settings.json` 已配好 Feishu + Dashboard 节

## 启动

```bash
dotnet run --project demo/AppHost
```

终端会打印 Aspire Dashboard 的 URL(通常 `http://localhost:1xxxx`),浏览器打开即可看到三资源状态、日志、健康红绿灯、OpenTelemetry 追踪/指标。前端入口取 `dashboard-client` 资源的 endpoint URL。

## 原理

- 两个 demo 项目沿用各自 `launchSettings.json` 的 http profile 端口(:5249 / :5080),并经 ServiceDefaults 暴露 `/health` `/alive`(仅 Development)。AppHost 用 `WithHttpHealthCheck("/health")` 探测。
- 前端经 `AddViteApp` 拉起;AppHost 用 `.WithEnvironment("VITE_API_BASE_URL", dashboard.GetEndpoint("http"))` 把后端地址喂给 `vite.config.ts` 的 proxy。
- 三者数据流不变:飞书写 `~/.man-in-black/maninblack.db`,Dashboard 以 `Mode=ReadOnly` 读(WAL 下并发安全)。

## 不走 Aspire

老流程仍完全可用:

```bash
dotnet run --project demo/FeishuAdaptor            # 飞书 bot
dotnet run --project demo/Dashboard                # Dashboard API
cd demo/Dashboard/client && npm run dev            # Vite(:5173,proxy 回落 :5080)
```

## 生产部署(Podman)

本地 Aspire AppHost 用于开发调试;生产走 **Aspire → Docker Compose → Podman** 容器化路径。一条命令生成 compose + 镜像,产出的 `docker-compose.yaml` 直接生产可用,无需手改。

### 原理(全在 `demo/AppHost/Program.cs`)

- `AddDockerComposeEnvironment("prod").WithDashboard(false).ConfigureComposeFile(...)`:把所有容器级配置(`user` / `HOME` / bind mount / `cap_add` / `security_opt` / loopback 端口 / healthcheck)在代码里写死。`Service` 模型是完整 compose schema(`CapAdd`/`SecurityOpt`/`Volumes`/`Ports`/`Healthcheck`/`Environment` 都能设),生成的 compose 直接生产可用。
- 各项目 `.PublishAsDockerFile(c => c.WithDockerfile("../..", "demo/<X>/Dockerfile", null))`:让发布时用项目自带的 Dockerfile build(含 node/python/bubblewrap 等运行时依赖),而非 .NET SDK 默认的容器发布(那个基座没这些依赖,build 也会失败)。
- 飞书走 WebSocket 主动连飞书服务器,**无任何入站端口**(只有容器内部 expose + 容器内 healthcheck);Dashboard 仅绑 `127.0.0.1:5080`,公网不可达,经 SSH 隧道访问。

### 三步发布

```bash
# 1. 开发机:生成 compose + build 镜像 + docker save(脚本内部跑 aspire do prepare-prod)
deploy/build-prod.sh
#    产物:deploy/aspire-aliyun/dist/{docker-compose.yaml, .env, images.tar}

# 2. 传到服务器
scp deploy/aspire-aliyun/dist/{docker-compose.yaml,.env,images.tar} <server>:~/mib/

# 3. 服务器:加载镜像 + 拉起容器
ssh <server> 'cd ~/mib && podman load -i images.tar && podman compose -f docker-compose.yaml up -d'
```

> 关键区分:`aspire publish` 只生成 compose、**不 build**;`aspire do prepare-prod` 才 **build 镜像**(用 `WithDockerfile` 指定的 Dockerfile)。详见 [Deploy to Docker Compose](https://aspire.dev/deployment/docker-compose/)。

完整踩坑记录与回滚方案见 **[systemd → Aspire 迁移手册](migration-systemd-to-aspire.md)**。
