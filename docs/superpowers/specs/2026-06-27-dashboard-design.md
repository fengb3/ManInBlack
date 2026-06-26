# ManInBlack Dashboard 设计文档

- **日期**:2026-06-27
- **状态**:已批准,待实现
- **位置**:`demo/Dashboard/`(新建)

---

## 1. 背景与目标

后台数据存储已从 JSON 文件迁移到 SQLite(EF Core),会话消息存于 `SessionMessages.PayloadJson`(整条 `ChatMessage` 的 JSON)。目前没有可视化手段查看库里的聊天记录,只能直连 DB 看 JSON。

**目标**:在 `demo/` 下新增一个**团队部署常驻**的 Web 应用 **ManInBlack Dashboard**,通过浏览器只读查看会话消息、用户、并支持全文搜索。前端用 Vite + React + TypeScript。

---

## 2. 范围

**做**:
- 会话列表(按 SessionId 聚合的消息数、首末时间、关联用户)
- 单会话消息查看(role + 文本/工具调用/工具结果内容块)
- 用户视图(用户及其名下会话)
- 全文搜索(LIKE 命中片段)
- 应用内共享密码 + cookie 鉴权
- React SPA 前端(Vite + TypeScript)

**不做(YAGNI)**:
- 状态快照(`AgentStateSnapshots`)查看
- 会话导出(HTML/Markdown/JSON)
- 写操作(删除/编辑)——Dashboard 严格只读
- FTS5 全文索引(LIKE 够用,留作未来增强)
- 应用层速率限制(交给反向代理)
- 前端单元测试(v1 不做;后端逻辑层有测试覆盖即可)

---

## 3. 架构概览

独立 ASP.NET Core Minimal API 项目(`Microsoft.NET.Sdk.Web`),**只读**同一份 `~/.man-in-black/maninblack.db`(WAL 已启用,与 FeishuAdaptor 的并发读写安全)。前端为 Vite 构建的 React SPA,构建产物落到 `wwwroot/`,由 ASP.NET 托管静态文件;API 返回 JSON,React 通过 `fetch`(`credentials: 'include'`)消费。

```
浏览器(React SPA)
   │  fetch /api/* (cookie, credentials: include)
   ▼
Kestrel(Minimal API + Cookie 鉴权)
   ├── wwwroot/  ← Vite 构建产物(React SPA,gitignore)
   └── /api/*   → ChatHistoryQueries → IDbContextFactory<ManInBlackDbContext> → 只读 SQLite
```

**开发**:Vite dev server(:5173)proxy `/api` → ASP.NET(:5080)。
**生产**:`dotnet publish` 触发 npm 构建,产物入 wwwroot,单进程托管。

---

## 4. 项目结构

```
demo/Dashboard/
  Dashboard.csproj                # Microsoft.NET.Sdk.Web, net10.0
  Program.cs                      # WebApplication 宿主 + DI + 端点 + 鉴权装配
  Auth/
    DashboardOptions.cs           # { Password } 配置绑定
    AuthEndpoints.cs              # cookie 鉴权 + /api/login + /api/me
  Data/
    ChatHistoryQueries.cs         # 直接查 DbContext:会话列表/用户/搜索/单会话
    ReadModels.cs                 # SessionSummary / UserSummary / MessageView / SearchResult
    ChatMessageRenderer.cs        # ChatMessage → MessageView(纯映射,无 DB)
  Properties/launchSettings.json
  wwwroot/                        # Vite 构建产物(gitignore),ASP.NET UseStaticFiles 托管
  client/                         # Vite + React + TypeScript 前端源码
    package.json
    vite.config.ts                # dev proxy /api → :5080;build outDir → ../wwwroot
    tsconfig.json
    index.html                    # Vite 入口
    src/
      main.tsx
      App.tsx                     # 路由 + 鉴权门禁(/api/me 决定登录态)
      api.ts                      # fetch 封装(credentials: 'include')
      types.ts                    # 与 ReadModels.cs 对齐的 TS 类型
      components/
        Login.tsx
        Sidebar.tsx               # 会话/用户/搜索 三 tab
        MessageList.tsx
        MessageView.tsx           # role 徽章 + 内容块
        SearchPanel.tsx
        UsersPanel.tsx
```

---

## 5. 宿主与 DI

**关键决定:不调用完整的 `AddManInBlack()`。**

`AddManInBlack()` 会注册整套 Agent 运行时(`IChatClient`、Provider/ApiKey、MCP、工具、`AgentFactory` 等),Dashboard 完全不需要,且可能因未配 Provider 而启动失败。Dashboard 唯一依赖核心库的部分是 `IDbContextFactory<ManInBlackDbContext>` + `AgentStorageOptions.RootPath`。

因此 `Program.cs` 自行:

1. `builder.Configuration.AddManInBlackSettings();`(复用 `~/.man-in-black/settings.json` 配置源,与 FeishuAdaptor 一致)
2. `services.Configure<AgentStorageOptions>(builder.Configuration.GetSection("Storage"));`(缺省 `~/.man-in-black`),使 `IOptions<AgentStorageOptions>` 可解析
3. 直接注册 DbContextFactory(**只读连接串**,不加迁移拦截器):

```csharp
builder.Services.AddDbContextFactory<ManInBlackDbContext>((sp, o) =>
{
    var root = sp.GetRequiredService<IOptions<AgentStorageOptions>>().Value.RootPath;
    o.UseSqlite($"Data Source={Path.Combine(root, "maninblack.db")};Mode=ReadOnly");
});
```

4. **不调用** `MigrateManInBlackStorageAsync()`——建表/迁移是写入方(FeishuAdaptor)的职责
5. 所有查询 `AsNoTracking()`
6. 静态文件托管: `app.UseDefaultFiles(); app.UseStaticFiles(); app.MapFallbackToFile("index.html");`(SPA 路由回退)

---

## 6. 数据访问(`Data/ChatHistoryQueries.cs`)

直查 DbContext,核心库抽象(`ISessionStorage`/`IUserStorage`)**保持不动**(它们的契约本就是「Agent 运行期写/加载」,不该为 Dashboard 塞枚举方法)。

| 查询 | 实现 |
|---|---|
| 会话列表 | 按 `SessionId` 分组 `SessionMessages`:`Count()`、`Min(CreatedAt)`、`Max(CreatedAt)` → `SessionSummary`;再读 `Users.SessionIdsJson` 建 SessionId→UserId 映射,内存关联 |
| 用户列表 | 读 `Users` 表,反序列化 `SessionIdsJson` / `MetadataJson` → `UserSummary`(含会话数与会话 ID 列表) |
| 单会话消息 | 按 `Id` 排序读 `PayloadJson`,逐行反序列化 `ChatMessage`,经 `ChatMessageRenderer` 映射成 `MessageView` |
| 全文搜索 | `WHERE PayloadJson LIKE @q`(EF 参数化),返回 SessionId + 命中片段 + 时间 |

**`ChatMessageRenderer`(纯映射)**:把 `Microsoft.Extensions.AI.ChatMessage` 的内容块翻成前端友好 DTO:
- `TextContent` → 文本块(前端 markdown 渲染)
- `FunctionCallContent` → 工具调用块(工具名 + 参数 JSON)
- `FunctionResultContent` → 工具结果块(callId + 结果 JSON)
- 未知类型 → raw JSON 块(优雅降级)

映射放服务端,前端无需引用 M.E.AI 类型。单行 JSON 损坏时跳过该行 + 日志告警(沿用 `SqliteAgentStateStorage` 的 try/catch 模式)。

**ReadModels**:
- `SessionSummary { SessionId, MessageCount, FirstAt, LastAt, UserId }`
- `UserSummary { UserId, Metadata, SessionIds[] }`
- `MessageView { Role, Blocks[] }`,Block = `{ kind: "text"|"tool_call"|"tool_result"|"unknown", ... }`
- `SearchResult { SessionId, Snippet, CreatedAt }`

---

## 7. 鉴权

- `AddAuthentication(CookieDefaults.AuthenticationScheme).AddCookie(...)`,cookie 路径 `/`
- 配置:`settings.json` 新增节 `Dashboard: { Password }`(与 `Feishu` 节并列,同为 settings.json 敏感项)
- **Fail-closed**:`Password` 未配置或为空 → 启动抛异常退出,绝不暴露无密码的 Dashboard
- 密码比对用 `CryptographicOperations.FixedTimeEquals`(固定时长,防计时侧信道);明文存储与现有 AppSecret 模式一致(可选:未来支持预哈希值)
- **SPA 鉴权门禁**:React App 启动调匿名 `/api/me`,未登录 → 渲染 `<Login/>`;`POST /api/login { password }` 校验通过 → `SignInAsync` 发 cookie → 重载进入主界面;`POST /api/logout` 清除
- fetch 全程 `credentials: 'include'`;dev 跨端口由 Vite proxy 同源化,生产同源,cookie 正常携带
- `/api/*`(除 `/api/me`、`/api/login`)`RequireAuthorization()`;SPA 外壳与静态资源可匿名访问,但无 API 数据无意义

---

## 8. API 端点

| 方法 路径 | 鉴权 | 返回 |
|---|---|---|
| `GET /api/me` | 匿名 | `{ authenticated: bool }`(SPA 探测登录态) |
| `POST /api/login` | 匿名 | 校验密码,设 cookie |
| `POST /api/logout` | 需登录 | 清除 cookie |
| `GET /api/sessions` | 需登录 | `SessionSummary[]` |
| `GET /api/sessions/{id}/messages` | 需登录 | `MessageView[]` |
| `GET /api/users` | 需登录 | `UserSummary[]` |
| `GET /api/search?q=` | 需登录 | `SearchResult[]` |
| 静态文件 `/` | 匿名(SPA 外壳) | wwwroot 产物 + `index.html` 回退 |

---

## 9. 前端(Vite + React + TypeScript)

**结构**:见第 4 节 `client/`。

- **鉴权门禁**:`App.tsx` 启动调 `/api/me`,401/`authenticated:false` → `<Login/>`;登录成功重载
- **布局**(同设计阶段确定的 ASCII):左 sidebar(会话/用户/搜索 三 tab)+ 右主消息区
  - **会话**:`SessionSummary` 列表(id 缩写 + 消息数 + 末次时间),点击 → 加载消息
  - **用户**:`UserSummary` 列表(UserId + 会话数),展开 → 名下会话,点会话 → 加载消息
  - **搜索**:输入框 → 命中列表(会话 id + 片段 + 时间),点击 → 跳转并高亮命中
- **消息渲染**(逐条):role 徽章(user/assistant/tool/system)+ 内容块;默认滚到最底
  - 文本块 → `react-markdown`(无需 `dangerouslySetInnerHTML`,安全)
  - 工具调用/结果 → 可折叠卡片(受控或 `<details>`),JSON 用 `<pre>{JSON.stringify(...)}</pre>`
  - 未知类型 → 灰色 `<details>` raw JSON
- **依赖**:`react` / `react-dom` / `react-markdown`,npm 安装,**不走 CDN**;`wwwroot/` 为构建产物,**gitignore**
- **XSS**:React 默认转义插值 + react-markdown 安全渲染 + 工具 JSON 走 `JSON.stringify`,无注入面
- **Vite 配置**:dev `proxy: { '/api': 'http://localhost:5080' }`;build `outDir: '../wwwroot', emptyOutDir: true`

---

## 10. 错误处理

- **单行 JSON 损坏**:跳过该行 + 日志告警,不崩溃整个列表
- **DB 不存在/不可读**:只读连接首次查询抛 → 端点 catch 返 503 + 清晰信息,React 顶部显示「无法读取数据库」横幅(团队部署下 DB 由 FeishuAdaptor 创建,正常不触发)
- **搜索**:空查询 → 空结果;LIKE 走 EF 参数化,无注入风险
- **鉴权**:密码错 → 401 可重试
- **前端**:fetch 失败/非 2xx → 各面板显示错误态,不白屏

---

## 11. 测试(`test/Dashboard.Tests`,xunit + 手写 fake)

- `ChatMessageRendererTests`(纯逻辑,无 DB):构造含 `TextContent`/`FunctionCallContent`/`FunctionResultContent`/未知类型的 `ChatMessage`,断言 `MessageView` 块正确
- `ChatHistoryQueriesTests`(临时 SQLite 文件):种入正常行 + 一行损坏 JSON,断言会话分组摘要、用户列表与会话映射、搜索 LIKE 命中、损坏行跳过不崩
- `AuthTests`:密码正确/错误比对、`Password` 为空时 fail-closed(启动抛异常)
- 端点是薄包装,主覆盖在查询+映射层;必要时补 1-2 个 `WebApplicationFactory` 冒烟测试

---

## 12. 文档与部署

**文档(CLAUDE.md 约定:改模块同步更新 docs)**:
- 新增 `docs/dashboard-guide.md`(用途、配置、运行、部署、安全说明)
- 更新 `CLAUDE.md`:加构建/运行命令(含 npm)+ 文档索引条目
- `docs/storage-guide.md` 加一行指向 Dashboard(可选)

**开发**:
```bash
dotnet run --project demo/Dashboard            # ASP.NET API :5080
cd demo/Dashboard/client && npm run dev        # Vite :5173,proxy /api → :5080
```

**发布**:
```bash
dotnet publish demo/Dashboard -c Release       # MSBuild target 自动 npm ci && npm run build → wwwroot
```
- `Dashboard.csproj` 含 MSBuild target(`BeforeTargets="Publish"`):在 client/ 跑 `npm ci` + `npm run build`,产物落 wwwroot
- **发布机需装 Node**;运行时是纯静态文件 + .NET,无需 Node

**部署**:沿用 FeishuAdaptor 的 `publish linux-x64` + systemd + 反向代理模式;应用层密码之外可再叠一层代理 basic auth。细节落到 `dashboard-guide.md`。

---

## 13. 配置示例(settings.json 新增节)

```json
{
  "Storage": { "RootPath": "~/.man-in-black" },
  "Dashboard": { "Password": "a-long-random-shared-secret" }
}
```

---

## 14. 未来增强(明确不在本期)

- FTS5 全文索引(替 LIKE,提速中文搜索)
- 状态快照查看(`AgentStateSnapshots`)
- 会话导出(Markdown/JSON)
- 写操作(删除/清理旧会话)
- 前端单元测试(Vitest + React Testing Library)
- 预哈希密码、OIDC/SSO
