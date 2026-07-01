# Aspire AppHost:同时启动 FeishuAdaptor + Dashboard — 设计

- **日期:** 2026-06-28
- **状态:** 设计已批准,待 spec review
- **Aspire 版本:** 13.4.6(最新稳定,与 .NET 10 对齐)

## 背景与目标

当前本地开发要分别开三个终端:

```bash
dotnet run --project demo/FeishuAdaptor          # 飞书 bot,:5249
dotnet run --project demo/Dashboard              # Dashboard API,:5080
cd demo/Dashboard/client && npm run dev          # Vite 前端,:5173
```

目标:新增一个 Aspire AppHost,`dotnet run --project demo/AppHost` 一条命令拉起三者,并在 Aspire Dashboard 统一查看日志、健康检查、遥测。

三者本就通过共享 `~/.man-in-black/maninblack.db` + `settings.json` 协作,**彼此无 HTTP 互调**。Aspire 在此只做进程编排 + 可观测性,不改数据流。

## 范围

**包含:**

- 新增 AppHost + ServiceDefaults 两个项目(放 `demo/`,沿用仓库"可运行项目都在 demo/"约定)
- AppHost 编排 `feishu` / `dashboard` / `dashboard-client`(Vite)三个资源
- 两个现有项目各加一行 `builder.AddServiceDefaults();` + 引用 ServiceDefaults
- Vite proxy 改读 Aspire 注入的后端地址(无则回落 :5080,**保持非 Aspire 老流程不变**)
- 新增 `docs/aspire-guide.md`,两个新项目登记进 `ManInBlack.slnx` 的 `/Demo/`

**不包含(YAGNI):**

- 不起 Redis / DB 容器(三者共用本地 SQLite,无需 Aspire 资源)
- 不动飞书 / Dashboard 业务逻辑
- 不做生产 manifest 发布(npm 资源 `ExcludeFromManifest`)

## 环境前置(已就绪)

- .NET SDK **10.0.300** ✅
- `aspire` CLI **13.4.6**(本次新装:`dotnet tool install --global aspire.cli`)✅

## 包版本(全部 NuGet 核实,钉最新稳定)

| 包 | 版本 | 用途 |
|----|------|------|
| `Aspire.AppHost.Sdk` | 13.4.6 | AppHost 的 SDK(`<Sdk Name="Aspire.AppHost.Sdk" Version="13.4.6" />`) |
| `Aspire.Hosting.AppHost` | 13.4.6 | AppHost 核心 API(`DistributedApplication`、`AddProject`) |
| `Aspire.Hosting.JavaScript` | 13.4.6 | `AddNpmApp`(Aspire 13 由旧 `Aspire.Hosting.NodeJs` 改名而来) |
| ServiceDefaults | —(模板生成) | **非 NuGet 包**;由 `aspire-servicedefaults` 模板生成,引用 OpenTelemetry / health / resilience 系列包,并生成 `AddServiceDefaults()` 扩展 |

**易踩坑(NuGet 已核实):**

- 旧名 `Aspire.Hosting.NodeJs`(止于 **9.5.2**)是遗留包,**Aspire 13 弃用**,装它会与 AppHost 13.x 版本不兼容。
- `Aspire.ServiceDefaults` **不存在为独立 NuGet 包**(flat API 返回 404);ServiceDefaults 是模板生成的类库。
- Aspire 13 对 JS 宿主做过**破坏性改名与参数调整**;`AddNpmApp` 的最终签名以 `Aspire.Hosting.JavaScript` 13.4.6 实际为准(实现时用 dotnet-library-viewer 核实)。

## 架构

```
dotnet run --project demo/AppHost
  └─ Aspire AppHost  (Aspire Dashboard URL:启动时终端打印)
       ├─ feishu            AddProject<FeishuAdaptor>   :5249  + /health 健康检查
       ├─ dashboard         AddProject<Dashboard>        :5080
       └─ dashboard-client  AddNpmApp(client, "dev")     :5173  ← WithReference(dashboard)
```

数据流不变:FeishuAdaptor 写 `maninblack.db` → Dashboard 以 `Mode=ReadOnly` 读同一 DB(WAL 下并发安全)。

## 组件设计

### 1. `demo/AppHost/ManInBlack.AppHost.csproj`

- `<Project Sdk="Microsoft.NET.Sdk">`,`<Sdk Name="Aspire.AppHost.Sdk" Version="13.4.6" />`
- `<PackageReference Include="Aspire.Hosting.AppHost" Version="13.4.6" />`
- `<PackageReference Include="Aspire.Hosting.JavaScript" Version="13.4.6" />`
- `<PropertyGroup>`:`net10.0`、`OutputType=Exe`、`ImplicitUsings=enable`、`Nullable=enable`、`IsAspireHost=true`

### 2. `demo/AppHost/Program.cs`(设计级,签名以实际为准)

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var feishu = builder.AddProject<Projects.FeishuAdaptor>("feishu")
    .WithHttpEndpoint(port: 5249)
    .WithHttpHealthCheck("/health")                       // 复用现有 /health
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development");

var dashboard = builder.AddProject<Projects.Dashboard>("dashboard")
    .WithHttpEndpoint(port: 5080)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development");

builder.AddNpmApp("dashboard-client", "../Dashboard/client", "dev")
    .WithReference(dashboard)                             // 注入 services__dashboard__http__0
    .WithHttpEndpoint(port: 5173)
    .ExcludeFromManifest();

builder.Build().Run();
```

> 端口确定性策略:显式 `WithHttpEndpoint(port)` + `ASPNETCORE_ENVIRONMENT=Development`,避免依赖 launchSettings 的 https / WSL profile(规避 dev cert 弹窗与 profile 歧义)。具体"禁用 launchSettings"的 API 名称实现时确认。

### 3. `demo/AppHost.ServiceDefaults/ManInBlack.AppHost.ServiceDefaults.csproj`

- 由模板生成(`dotnet new aspire-servicedefaults -o demo/AppHost.ServiceDefaults`,或 `aspire new` 选 starter 后裁剪),自动钉与 13.4.6 兼容的版本
- 内含 `AddServiceDefaults()` 扩展:OpenTelemetry OTLP 导出 + `/health` `/alive` 端点 + http resilient handler

### 4. 现有项目改动(最小)

- **`demo/FeishuAdaptor/FeishuAdaptor.csproj`、`demo/Dashboard/Dashboard.csproj`**:各加一行
  ```xml
  <ProjectReference Include="..\AppHost.ServiceDefaults\ManInBlack.AppHost.ServiceDefaults.csproj" />
  ```
- **`demo/FeishuAdaptor/Program.cs`**:在 `var builder = WebApplication.CreateBuilder(args);` 之后加
  ```csharp
  builder.AddServiceDefaults();
  ```
  (FeishuAdaptor 已有 `/health` 端点,ServiceDefaults 自动暴露 `/health` `/alive`)
- **`demo/Dashboard/Program.cs`**:加 `builder.AddServiceDefaults();` + `builder.Services.AddHealthChecks();`(纯 liveness,无外部依赖探测)
- **`demo/Dashboard/client/vite.config.ts`**:
  ```ts
  import { defineConfig } from 'vite'
  import react from '@vitejs/plugin-react'

  const api = process.env['services__dashboard__http__0'] ?? 'http://localhost:5080'

  export default defineConfig({
    plugins: [react()],
    server: { proxy: { '/api': api } },
    build: { outDir: '../wwwroot', emptyOutDir: true },
  })
  ```
  → Aspire `WithReference(dashboard)` 注入该环境变量;非 Aspire 直跑 Vite 时回落 :5080,**老工作流不变**。

### 5. 文档与 slnx

- 新增 `docs/aspire-guide.md`:一条命令起三者 + Aspire Dashboard 说明 + 端口表 + node_modules 首装提示
- `ManInBlack.slnx`:在 `/Demo/` 下登记
  ```xml
  <Project Path="demo/AppHost/ManInBlack.AppHost.csproj" />
  <Project Path="demo/AppHost.ServiceDefaults/ManInBlack.AppHost.ServiceDefaults.csproj" />
  ```
- 按 CLAUDE.md 约定同步更新 `docs/quick-start.md`(加入 Aspire 启动方式)。

## 端口

| 资源 | 端口 | 备注 |
|------|------|------|
| feishu | 5249 | 与现有 launchSettings http profile 一致;WebSocket 模式无需入站公网 |
| dashboard | 5080 | 与现有一致 |
| dashboard-client(Vite) | 5173 | 浏览器入口 |
| Aspire Dashboard | 自动分配 | AppHost 启动时终端打印 URL(或自动开浏览器) |

## 测试策略

纯编排 + 配置,**无新增单元测试**。手动验收清单:

1. `dotnet run --project demo/AppHost` 成功构建启动,Aspire Dashboard 显示三资源 `Running`
2. feishu 健康检查**绿灯**(`/health` 200)
3. 浏览器开 Vite(:5173)→ Dashboard 登录页正常;`/api/*` 返回 200(验证 Vite proxy 命中 :5080)
4. 飞书发消息 → bot 响应,且 Dashboard 能查到新会话(验证 DB 共享未受影响)

**回归保障:** 不走 Aspire 时,`dotnet run --project demo/Dashboard` + `npm run dev` 老流程仍可用(vite proxy 回落 :5080);FeishuAdaptor 单独 `dotnet run` 不受影响。

## 风险与决策

- **AddNpmApp 签名(Aspire 13 改名)**:`Aspire.Hosting.NodeJs`→`Aspire.Hosting.JavaScript`,`AddNodeApp` 移除、参数顺序有调整。实现时核实 `AddNpmApp` 实际签名再调用。
- **node_modules**:`AddNpmApp` 起 Vite 前需 `client/node_modules` 存在;已存在则跳过,文档提示首次 `npm install`。
- **launchSettings 歧义**:AppHost 显式锁端口 + Development,规避 https/WSL profile 与 dev cert 弹窗。
- **DB 并发**:FeishuAdaptor(写)与 Dashboard(只读,WAL)已验证并发安全,Aspire 不改变此模型。

## 实现顺序(高层)

1. 安装 Aspire 模板 / 或手写 csproj(钉 13.4.6)
2. 建 `demo/AppHost` + `demo/AppHost.ServiceDefaults` 两个项目
3. 改两个现有项目(Program.cs + csproj)
4. 改 `demo/Dashboard/client/vite.config.ts`
5. 登记 `ManInBlack.slnx` + 写 `docs/aspire-guide.md` + 更新 quick-start
6. 手动跑 4 步验收
