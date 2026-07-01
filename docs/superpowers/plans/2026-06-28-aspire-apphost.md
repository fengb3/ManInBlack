# Aspire AppHost 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 新增一个 Aspire AppHost,一条 `dotnet run` 同时启动 FeishuAdaptor(飞书 bot)、Dashboard API、Dashboard Vite 前端,并在 Aspire Dashboard 统一查看日志/健康/遥测。

**Architecture:** 经典 csproj 式 AppHost(`Aspire.AppHost.Sdk` + `IsAspireHost`),通过 `AddProject` 引用两个现有 demo 项目、`AddViteApp` 拉起前端 dev server,前端经 Vite 服务器端 proxy 打到 Dashboard 后端。两个现有项目引用模板生成的 ServiceDefaults(OpenTelemetry + 健康检查)。三者数据流不变(共享 `~/.man-in-black/maninblack.db`)。

**Tech Stack:** .NET 10 SDK 10.0.300、Aspire 13.4.6(`Aspire.AppHost.Sdk`、`Aspire.Hosting.AppHost`、`Aspire.Hosting.JavaScript`)、Vite 6 + React 18。

## Global Constraints

(本节为全部任务的隐含要求,逐字来自 spec)

- **Aspire 版本钉死 13.4.6**:`Aspire.AppHost.Sdk`、`Aspire.Hosting.AppHost`、`Aspire.Hosting.JavaScript` 全部 `13.4.6`。**禁用** 遗留包 `Aspire.Hosting.NodeJs`(止于 9.5.2)。
- **Vite 用 `AddViteApp`**,不是 `AddNpmApp`。`AddViteApp` 自动注册 http endpoint + `PORT` 环境变量 —— **禁止**对其再调 `.WithHttpEndpoint()`(会 duplicate endpoint 报错)。
- 后端 URL 经 `.WithEnvironment("VITE_API_BASE_URL", dashboard.GetEndpoint("http"))` 注入;vite.config.ts 读 `process.env.VITE_API_BASE_URL`,回落 `http://localhost:5080`。
- 注释/文档一律中文;提交信息用 gitmoji 前缀,**禁止** `Co-authored-by`。
- 不动飞书 / Dashboard 业务逻辑;不起 Redis/DB 容器(共用本地 SQLite)。
- **无新增单元测试**(纯编排 + 配置,经 spec 确认)。每个任务的"验证"= `dotnet build` 编译通过;最终任务 = 完整 `dotnet run` 手动验收。

---

## File Structure

| 文件 | 动作 | 职责 |
|------|------|------|
| `demo/AppHost.ServiceDefaults/ManInBlack.AppHost.ServiceDefaults.csproj` | 新建(模板生成) | ServiceDefaults 类库:OTEL + 健康检查 + http resilience |
| `demo/AppHost.ServiceDefaults/Extensions.cs` | 新建(模板生成) | `AddServiceDefaults()` + `MapDefaultEndpoints()` 扩展 |
| `demo/AppHost/ManInBlack.AppHost.csproj` | 新建(手写) | AppHost 项目,引用两个 demo + JS 宿主包 |
| `demo/AppHost/Program.cs` | 新建(手写) | 编排 feishu / dashboard / dashboard-client 三资源 |
| `demo/FeishuAdaptor/FeishuAdaptor.csproj` | 修改 | 加 ServiceDefaults 引用 |
| `demo/FeishuAdaptor/Program.cs` | 修改 | `AddServiceDefaults` + `MapDefaultEndpoints`,删自定义 `/health` |
| `demo/Dashboard/Dashboard.csproj` | 修改 | 加 ServiceDefaults 引用 |
| `demo/Dashboard/Program.cs` | 修改 | `AddServiceDefaults` + `MapDefaultEndpoints` |
| `demo/Dashboard/client/vite.config.ts` | 修改 | proxy target + port 改读环境变量 |
| `ManInBlack.slnx` | 修改 | `/Demo/` 登记两个新项目 |
| `docs/aspire-guide.md` | 新建 | Aspire 编排使用文档 |
| `CLAUDE.md` | 修改 | 构建命令 + 文档索引 |
| `docs/dashboard-guide.md` | 修改 | 开发小节补 Aspire 一行 |

---

## Task 1: 用模板生成 ServiceDefaults 项目并登记进 slnx

**Files:**
- Create: `demo/AppHost.ServiceDefaults/ManInBlack.AppHost.ServiceDefaults.csproj`(模板生成)
- Create: `demo/AppHost.ServiceDefaults/Extensions.cs`(模板生成)
- Modify: `ManInBlack.slnx`(`/Demo/` 加一行)

**Interfaces:**
- Produces: 命名空间 `Microsoft.Extensions.Hosting` 下的 `AddServiceDefaults<TBuilder>(this TBuilder)` 与 `MapDefaultEndpoints(this WebApplication)`。后续 Task 3/4 消费这两个方法。`AddServiceDefaults` 配置 OTEL + 健康检查 + http resilience;`MapDefaultEndpoints` 仅在 Development 下 map `/health` `/alive`。

- [ ] **Step 1: 用 aspire-servicedefaults 模板生成项目**

在仓库根目录执行(Aspire 模板已在 spec 阶段安装 `Aspire.ProjectTemplates::13.4.6`):

```bash
dotnet new aspire-servicedefaults -o demo/AppHost.ServiceDefaults -n ManInBlack.AppHost.ServiceDefaults
```

预期:生成 `demo/AppHost.ServiceDefaults/ManInBlack.AppHost.ServiceDefaults.csproj` 与 `demo/AppHost.ServiceDefaults/Extensions.cs`,末尾打印 `已成功还原。`。

- [ ] **Step 2: 确认模板生成的 csproj 内容正确**

打开 `demo/AppHost.ServiceDefaults/ManInBlack.AppHost.ServiceDefaults.csproj`,应与下面一致(`TargetFramework=net10.0`、`IsAspireSharedProject=true`):

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsAspireSharedProject>true</IsAspireSharedProject>
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />

    <PackageReference Include="Microsoft.Extensions.Http.Resilience" Version="10.6.0" />
    <PackageReference Include="Microsoft.Extensions.ServiceDiscovery" Version="10.6.0" />
    <PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.15.3" />
    <PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.15.3" />
    <PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.15.2" />
    <PackageReference Include="OpenTelemetry.Instrumentation.Http" Version="1.15.1" />
    <PackageReference Include="OpenTelemetry.Instrumentation.Runtime" Version="1.15.1" />
  </ItemGroup>

</Project>
```

> 若模板生成的版本号有差异(模板小版本升级),保留模板版本即可,无需手改。

- [ ] **Step 3: 把项目登记进 ManInBlack.slnx 的 `/Demo/`**

编辑 `ManInBlack.slnx`,在 `/Demo/` Folder 内、`Playground` 行之后插入一行:

旧:
```xml
    <Project Path="demo/Playground/Playground.csproj" />
  </Folder>
```
新:
```xml
    <Project Path="demo/Playground/Playground.csproj" />
    <Project Path="demo/AppHost.ServiceDefaults/ManInBlack.AppHost.ServiceDefaults.csproj" />
  </Folder>
```

- [ ] **Step 4: 编译验证**

```bash
dotnet build demo/AppHost.ServiceDefaults/ManInBlack.AppHost.ServiceDefaults.csproj
```
预期:`Build succeeded`,0 error。

- [ ] **Step 5: 提交**

```bash
git add demo/AppHost.ServiceDefaults ManInBlack.slnx
git commit -m "✨ 新增 Aspire ServiceDefaults 项目(OTEL + 健康检查)"
```

---

## Task 2: 创建 AppHost 项目(csproj + Program.cs)并登记进 slnx

**Files:**
- Create: `demo/AppHost/ManInBlack.AppHost.csproj`(手写)
- Create: `demo/AppHost/Program.cs`(手写)
- Modify: `ManInBlack.slnx`(`/Demo/` 再加一行)

**Interfaces:**
- Consumes: `Projects.FeishuAdaptor`、`Projects.Dashboard`(由 `Aspire.AppHost.Sdk` 根据 ProjectReference 自动生成的静态访问器)。
- Produces: 可执行 AppHost。`dotnet run --project demo/AppHost` 启动三个资源。
- 暴露给前端:`VITE_API_BASE_URL` 环境变量 = dashboard 的 http endpoint URL。

- [ ] **Step 1: 写 AppHost.csproj**

创建 `demo/AppHost/ManInBlack.AppHost.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <Sdk Name="Aspire.AppHost.Sdk" Version="13.4.6" />

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsAspireHost>true</IsAspireHost>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Aspire.Hosting.AppHost" Version="13.4.6" />
    <PackageReference Include="Aspire.Hosting.JavaScript" Version="13.4.6" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\FeishuAdaptor\FeishuAdaptor.csproj" />
    <ProjectReference Include="..\Dashboard\Dashboard.csproj" />
  </ItemGroup>

</Project>
```

> `<Sdk Name="Aspire.AppHost.Sdk" />` 引入 SDK,自动生成 `Projects` 静态类与 AppHost MSBuild 逻辑;`IsAspireHost=true` 标记本项目为 host。

- [ ] **Step 2: 写 Program.cs**

创建 `demo/AppHost/Program.cs`:

```csharp
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
```

> 若编译报 `AddViteApp`/`WithEnvironment` 找不到命名空间,加 `using Aspire.Hosting;`。通常 AppHost SDK 的全局 using 已覆盖,无需手加。

- [ ] **Step 3: 登记进 ManInBlack.slnx**

编辑 `ManInBlack.slnx`,在 `/Demo/` 内、刚加的 ServiceDefaults 行之后插入:

旧:
```xml
    <Project Path="demo/AppHost.ServiceDefaults/ManInBlack.AppHost.ServiceDefaults.csproj" />
  </Folder>
```
新:
```xml
    <Project Path="demo/AppHost.ServiceDefaults/ManInBlack.AppHost.ServiceDefaults.csproj" />
    <Project Path="demo/AppHost/ManInBlack.AppHost.csproj" />
  </Folder>
```

- [ ] **Step 4: 编译验证(AppHost 首次 restore + 编译)**

```bash
dotnet build demo/AppHost/ManInBlack.AppHost.csproj
```
预期:`Build succeeded`。首次会 restore `Aspire.AppHost.Sdk`/`Aspire.Hosting.AppHost`/`Aspire.Hosting.JavaScript`。

> 若报 `Projects.FeishuAdaptor` 找不到:确认 csproj 里的两个 `<ProjectReference>` 路径正确(相对 `demo/AppHost/`,即 `..\FeishuAdaptor\...` 与 `..\Dashboard\...`)。SDK 据此生成 `Projects` 类。

- [ ] **Step 5: 提交**

```bash
git add demo/AppHost ManInBlack.slnx
git commit -m "✨ 新增 Aspire AppHost:编排 feishu/dashboard/前端三资源"
```

---

## Task 3: 把 ServiceDefaults 接入 FeishuAdaptor

**Files:**
- Modify: `demo/FeishuAdaptor/FeishuAdaptor.csproj`
- Modify: `demo/FeishuAdaptor/Program.cs`

**Interfaces:**
- Consumes: `builder.AddServiceDefaults()`、`app.MapDefaultEndpoints()`(来自 Task 1)。

- [ ] **Step 1: csproj 加 ProjectReference**

编辑 `demo/FeishuAdaptor/FeishuAdaptor.csproj`,在现有 `</ItemGroup>` 之前的最后一个 ProjectReference ItemGroup(含 `..\..\src\ManInBlack.AI\...`)之后,新增一个 ItemGroup:

在
```xml
  <ItemGroup>
    <ProjectReference Include="..\..\src\ManInBlack.AI\ManInBlack.AI.csproj" />
    <ProjectReference Include="..\..\src\ManInBlack.AI.SourceGenerator\ManInBlack.AI.SourceGenerator.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  </ItemGroup>
```
之后追加:
```xml
  <ItemGroup>
    <ProjectReference Include="..\AppHost.ServiceDefaults\ManInBlack.AppHost.ServiceDefaults.csproj" />
  </ItemGroup>
```

- [ ] **Step 2: Program.cs 加 AddServiceDefaults**

编辑 `demo/FeishuAdaptor/Program.cs`。在
```csharp
var builder = WebApplication.CreateBuilder(args);
```
之后紧接着插入一行:
```csharp
builder.AddServiceDefaults();
```

- [ ] **Step 3: 用 MapDefaultEndpoints 替换自定义 /health**

FeishuAdaptor 自定义的 `app.MapGet("/health", ...)` 会与 ServiceDefaults 在 Development 下 map 的 `/health` 撞路由,必须移除,改用 `MapDefaultEndpoints`。

编辑 `demo/FeishuAdaptor/Program.cs`,把这段
```csharp
// 愿你健康, 开心, 美满, 幸福
app.MapGet(
    "/health",
    () =>
    {
        // returns random health status for demonstration purposes
        string[] healthyTexts = ["feeling great!", "ready to serve!", "fully operational!"];
        var random = new Random();
        var text = healthyTexts[random.Next(healthyTexts.Length)];
        return Results.Ok(new { status = "healthy", message = text });
    }
);
```
整段替换为:
```csharp
// /health、/alive 端点由 ServiceDefaults 提供(仅 Development)。Aspire 据此做健康探测。
app.MapDefaultEndpoints();
```

- [ ] **Step 4: 编译验证**

```bash
dotnet build demo/FeishuAdaptor/FeishuAdaptor.csproj
```
预期:`Build succeeded`。

- [ ] **Step 5: 提交**

```bash
git add demo/FeishuAdaptor/FeishuAdaptor.csproj demo/FeishuAdaptor/Program.cs
git commit -m "✨ FeishuAdaptor 接入 ServiceDefaults,移除自定义 /health"
```

---

## Task 4: 把 ServiceDefaults 接入 Dashboard

**Files:**
- Modify: `demo/Dashboard/Dashboard.csproj`
- Modify: `demo/Dashboard/Program.cs`

**Interfaces:**
- Consumes: `builder.AddServiceDefaults()`、`app.MapDefaultEndpoints()`(来自 Task 1)。

- [ ] **Step 1: csproj 加 ProjectReference**

编辑 `demo/Dashboard/Dashboard.csproj`,在现有 ProjectReference ItemGroup
```xml
  <ItemGroup>
    <ProjectReference Include="..\..\src\ManInBlack.AI\ManInBlack.AI.csproj" />
  </ItemGroup>
```
之后追加:
```xml
  <ItemGroup>
    <ProjectReference Include="..\AppHost.ServiceDefaults\ManInBlack.AppHost.ServiceDefaults.csproj" />
  </ItemGroup>
```

- [ ] **Step 2: Program.cs 加 AddServiceDefaults**

编辑 `demo/Dashboard/Program.cs`。在
```csharp
var builder = WebApplication.CreateBuilder(args);
```
之后紧接着插入一行:
```csharp
builder.AddServiceDefaults();
```

- [ ] **Step 3: Program.cs 加 MapDefaultEndpoints**

编辑 `demo/Dashboard/Program.cs`。在
```csharp
app.UseAuthorization();
```
之后紧接着插入一行:
```csharp
app.MapDefaultEndpoints();
```

> Dashboard 的 `app.MapFallbackToFile("index.html")` 是兜底路由,具体路由(`/health`、`/api/...`)优先匹配,无冲突。`/health` 未挂 `RequireAuthorization`,鉴权中间件放行匿名,Aspire 可直探。

- [ ] **Step 4: 编译验证**

```bash
dotnet build demo/Dashboard/Dashboard.csproj
```
预期:`Build succeeded`。

- [ ] **Step 5: 提交**

```bash
git add demo/Dashboard/Dashboard.csproj demo/Dashboard/Program.cs
git commit -m "✨ Dashboard 接入 ServiceDefaults(/health、/alive)"
```

---

## Task 5: vite.config.ts 改读环境变量

**Files:**
- Modify: `demo/Dashboard/client/vite.config.ts`

**Interfaces:**
- Consumes: `VITE_API_BASE_URL`(Task 2 的 AppHost 注入)、`PORT`(AddViteApp 注入)。

- [ ] **Step 1: 重写 vite.config.ts**

把 `demo/Dashboard/client/vite.config.ts` 整文件替换为:

```ts
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// 后端地址:Aspire 经 VITE_API_BASE_URL 注入;单独 `npm run dev` 时回落 :5080。
const api = process.env['VITE_API_BASE_URL'] ?? 'http://localhost:5080'
// 端口:Aspire 的 AddViteApp 注入 PORT;单独跑时回落 5173。
const port = Number(process.env['PORT']) || 5173

export default defineConfig({
  plugins: [react()],
  server: { port, host: true, proxy: { '/api': api } },
  build: { outDir: '../wwwroot', emptyOutDir: true },
})
```

> proxy 跑在 Vite 的 Node 进程里(服务端),故读 `process.env` 不受 `VITE_` 前缀限制;`host: true` 让 Aspire 能访问到。非 Aspire 老流程(`npm run dev`)因 env 未设而回落原值,行为不变。

- [ ] **Step 2: 校验 TS 能过(可选但推荐)**

```bash
cd demo/Dashboard/client && npm run build
```
预期:`vite build` 成功,产物落到 `demo/Dashboard/wwwroot/`。(这一步同时验证 vite.config.ts 语法/类型无误。)

- [ ] **Step 3: 提交**

```bash
git add demo/Dashboard/client/vite.config.ts
git commit -m "✨ vite.config 读 VITE_API_BASE_URL/PORT(Aspire 注入,回落兼容老流程)"
```

---

## Task 6: 文档

**Files:**
- Create: `docs/aspire-guide.md`
- Modify: `CLAUDE.md`(构建命令 + 文档索引)
- Modify: `docs/dashboard-guide.md`(开发小节)

> 文档放置说明(相对 spec 的微调):Aspire 运行说明归入新建 `docs/aspire-guide.md` 与 `CLAUDE.md`(最易被发现的"构建与测试"区),并在 `dashboard-guide.md` 开发小节留一行指针。`docs/quick-start.md` 主题是"从零创建一个 agent",与"跑 demo"不是同一读者群,故不在其中加入 Aspire 内容。

- [ ] **Step 1: 写 docs/aspire-guide.md**

创建 `docs/aspire-guide.md`,内容:

````markdown
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
````

- [ ] **Step 2: CLAUDE.md 加构建命令**

编辑 `CLAUDE.md`,在"构建与测试"代码块的命令列表中,找到
```bash
dotnet run --project demo/Dashboard                            # Dashboard API（:5080）
```
之后插入一行:
```bash
dotnet run --project demo/AppHost                              # Aspire:同时启动飞书 + Dashboard + 前端
```

- [ ] **Step 3: CLAUDE.md 文档索引加 aspire-guide**

编辑 `CLAUDE.md`,在文档索引列表中,找到
```markdown
- [Dashboard 指南](docs/dashboard-guide.md)
```
之后插入:
```markdown
- [Aspire 编排指南](docs/aspire-guide.md)
```

- [ ] **Step 4: dashboard-guide.md 开发小节补一行**

编辑 `docs/dashboard-guide.md`,在"## 开发"小节代码块
```bash
# 前端 Vite（:5173，proxy /api → :5080）
cd demo/Dashboard/client && npm run dev
```
之后追加一段:
```markdown

> 也可用 Aspire 一条命令同时启动飞书 + Dashboard + 前端:`dotnet run --project demo/AppHost`。详见 [Aspire 编排指南](./aspire-guide.md)。
```

- [ ] **Step 5: 提交**

```bash
git add docs/aspire-guide.md docs/dashboard-guide.md CLAUDE.md
git commit -m "📝 新增 Aspire 编排指南,更新 CLAUDE.md/dashboard-guide"
```

---

## Task 7: 完整构建 + 运行验收

**Files:** 无(纯验证)。

- [ ] **Step 1: 整库编译**

```bash
dotnet build ManInBlack.slnx
```
预期:`Build succeeded`,0 error。(含 AppHost、ServiceDefaults、两个 demo、所有 src/test。)

- [ ] **Step 2: 确认前端依赖已装**

```bash
test -d demo/Dashboard/client/node_modules && echo "node_modules OK" || (cd demo/Dashboard/client && npm install)
```
预期:打印 `node_modules OK`(或完成 npm install)。

- [ ] **Step 3: 启动 AppHost**

```bash
dotnet run --project demo/AppHost
```
预期:终端打印类似
```
Login to the Aspire Dashboard at https://localhost:1xxxx/login?t=...
```
浏览器打开该 URL → Resources 页看到 `feishu`、`dashboard`、`dashboard-client` 三个均 `Running`。

- [ ] **Step 4: 健康检查绿灯**

在 Aspire Dashboard 的 Health 列,`feishu` 与 `dashboard` 应为绿色(`Healthy`)。
命令行旁路核对(端口以 AppHost 实际分配为准,dashboard 沿用 :5080):
```bash
curl -s http://localhost:5080/health
```
预期:`Healthy`。

- [ ] **Step 5: 前端可达且 proxy 命中后端**

打开 Aspire Dashboard 里 `dashboard-client` 资源的 endpoint URL(浏览器)→ 应渲染 Dashboard 登录页;用 `~/.man-in-black/settings.json` 里 `Dashboard.Password` 登录 → 会话列表正常加载(说明 `/api/*` 经 Vite proxy 打到了 :5080)。

- [ ] **Step 6(可选但推荐):端到端 DB 共享未受影响**

向飞书 bot 发一条消息 → bot 正常响应;回到 Dashboard 刷新 → 能看到这条新会话/消息(验证飞书写、Dashboard 读同一 SQLite 未受 Aspire 影响)。

> **风险关注点(来自 spec):** `AddServiceDefaults` 的 `ConfigureHttpClientDefaults` 会给所有经 `IHttpClientFactory` 创建的 HttpClient 加标准 resilience handler(retry/circuit-breaker/timeout)。飞书 SDK 与 AI provider 的 HTTP 调用若走 IHttpClientFactory,行为会变(更激进的重试)。若 Step 6 观察到飞书调用异常(重复请求 / 超时变化),缓解方案:编辑 `demo/AppHost.ServiceDefaults/Extensions.cs`,删除 `AddServiceDefaults` 内的 `builder.Services.ConfigureHttpClientDefaults(...)` 整块(仅保留 OTEL + 健康检查 + service discovery),重新验收。

- [ ] **Step 7: 收尾提交(若有验收中产生的微调)**

若验收中发现并修复了小问题(如补 using、调端口),提交之;否则跳过。

```bash
git add -A
git commit -m "✅ Aspire AppHost 验收通过"
```

---

## Self-Review 记录

- **Spec 覆盖**:三资源编排(Task 2)、ServiceDefaults 遥测/健康(Task 1+3+4)、vite.config 环境驱动(Task 5)、slnx 登记(Task 1+2)、docs(Task 6)、验收(Task 7)—— spec 各节均有任务对应。
- **占位符扫描**:无 TBD/TODO;所有代码块为完整可用内容;`AddViteApp`/`WithEnvironment` 签名取自 Aspire 13 官方文档(已核实)。
- **类型/命名一致性**:`AddServiceDefaults` / `MapDefaultEndpoints`(Task 1 定义)与 Task 3/4 调用一致;`VITE_API_BASE_URL`(Task 2 注入)与 Task 5 读取一致;`Projects.FeishuAdaptor`/`Projects.Dashboard`(Task 2)由 SDK 据 ProjectReference 生成。
- **已核实的实现细节**:`Aspire.Hosting.JavaScript` 13.4.6 提供 `AddViteApp`;ServiceDefaults 模板真实包为 `Microsoft.Extensions.Http.Resilience` + `Microsoft.Extensions.ServiceDiscovery` + OpenTelemetry(非 `Aspire.ServiceDefaults`,该包不存在);`/health` 路由冲突经移除 FeishuAdaptor 自定义端点解决。
