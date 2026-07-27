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

## 陷阱:`AddServiceDefaults` 的标准 resilience 会套住 LLM HttpClient

`AddServiceDefaults()`(`AppHost.ServiceDefaults/Extensions.cs`)默认对所有 HttpClient 调 `AddStandardResilienceHandler()`,其默认**每次尝试超时 30s** + 3 次指数退避重试。若 LLM 的 HttpClient 也被套上,后果:

- 推理模型首字节延迟 >30s 时,`HttpClient.SendAsync` 在等待响应头阶段被 Polly 砍断 → `Polly.Timeout.TimeoutRejectedException`,飞书侧表现为 `error when launch agent`。
- Polly 的重试与应用层 `RetryMiddleware`(3 次)叠加,最多重复请求 9 次,放大延迟与 LLM 计费。

**已在主库隔离(各 demo 无需处理)**:LLM `IChatClient` 走专属命名 HttpClient `ManInBlackHttpClients.ChatClient`(`"ManInBlack.Chat"`)。`AddManInBlack()` 注册时即 `RemoveAllResilienceHandlers()` 移除全局注入的标准 resilience,并设 30 分钟兜底超时;LLM 重试由 `RetryMiddleware` 统一负责。OpenTelemetry HTTP 观测不受影响(`AddHttpClientInstrumentation` hook 传输层,与 client 命名/resilience 无关)。详见 [Provider 配置指南 · LLM HttpClient](provider-guide.md#llm-httpclient)。

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

- `AddDockerComposeEnvironment("prod").WithDashboard(false).ConfigureComposeFile(...)`:把所有容器级配置(`user` / `HOME` / bind mount / `cap_add` / `security_opt` / loopback 端口 / healthcheck)在代码里写死。`Service` 模型是完整 compose schema(`CapAdd`/`SecurityOpt`/`Volumes`/`Ports`/`Healthcheck`/`Environment`/`NetworkMode`/`Networks` 都能设),生成的 compose 直接生产可用。
- 各项目 `.PublishAsDockerFile(c => c.WithDockerfile("../..", "demo/<X>/Dockerfile", null))`:让发布时用项目自带的 Dockerfile build(含 node/python/bubblewrap 等运行时依赖),而非 .NET SDK 默认的容器发布(那个基座没这些依赖,build 也会失败)。
- feishu 容器走 **host 网络模式**(`network_mode: host`):Agent 用内网 IP 访问同机其他容器/宿主机进程时,Aspire 默认的 bridge 网络会让跨网络流量超时(被丢弃);host 模式共享宿主网络栈后,宿主机能路由到本机所有 docker 网桥,故可直接通达。飞书 webhook 回调打宿主 `11411`(host 模式下容器直接监听宿主 `11411`,无端口映射)。Dashboard 仍走默认 bridge,仅绑 `127.0.0.1:5080`,公网不可达,经 SSH 隧道访问。
  - **坑**:`network_mode` 与 `networks` 互斥,Aspire 默认给每个 service 注入 `networks: [aspire]`,不清掉会让 host 模式失效。代码里必须 `feishu.Networks = null!`(并 `feishu.Expose = null!` 清掉默认注入的 `expose: 8080`)。生成后校验 feishu 段有 `network_mode: host` 且**无** `networks:` 键。

### 三步发布

```bash
# 1. 开发机:生成 compose + build 镜像 + docker save(脚本内部跑 aspire do prepare-prod)
deploy/build-prod.sh
#    产物:deploy/output/dist/{docker-compose.yaml, .env, images.tar}

# 2. 传到服务器
scp deploy/output/dist/{docker-compose.yaml,.env,images.tar} <server>:~/mib/

# 3. 服务器:加载镜像 + 拉起容器
ssh <server> 'cd ~/mib && podman load -i images.tar && podman compose -f docker-compose.yaml up -d'
```

> 关键区分:`aspire publish` 只生成 compose、**不 build**;`aspire do prepare-prod` 才 **build 镜像**(用 `WithDockerfile` 指定的 Dockerfile)。详见 [Deploy to Docker Compose](https://aspire.dev/deployment/docker-compose/)。

完整踩坑记录与回滚方案见 **[systemd → Aspire 迁移手册](migration-systemd-to-aspire.md)**。
