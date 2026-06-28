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
