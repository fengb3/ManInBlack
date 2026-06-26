# ChatViewer Web UI 设计文档

- **日期**:2026-06-27
- **状态**:已批准,待实现
- **位置**:`demo/ChatViewer/`(新建)

---

## 1. 背景与目标

后台数据存储已从 JSON 文件迁移到 SQLite(EF Core),会话消息存于 `SessionMessages.PayloadJson`(整条 `ChatMessage` 的 JSON)。目前没有可视化手段查看库里的聊天记录,只能直连 DB 看 JSON。

**目标**:在 `demo/` 下新增一个**团队部署常驻**的 Web 应用,通过浏览器只读查看会话消息、用户、并支持全文搜索。

---

## 2. 范围

**做**:
- 会话列表(按 SessionId 聚合的消息数、首末时间、关联用户)
- 单会话消息查看(role + 文本/工具调用/工具结果内容块)
- 用户视图(用户及其名下会话)
- 全文搜索(LIKE 命中片段)
- 应用内共享密码 + cookie 鉴权

**不做(YAGNI)**:
- 状态快照(`AgentStateSnapshots`)查看
- 会话导出(HTML/Markdown/JSON)
- 写操作(删除/编辑)——查看器严格只读
- FTS5 全文索引(LIKE 够用,留作未来增强)
- 应用层速率限制(交给反向代理)

---

## 3. 架构概览

独立 ASP.NET Core Minimal API 项目(`Microsoft.NET.Sdk.Web`),**只读**同一份 `~/.man-in-black/maninblack.db`(WAL 已启用,与 FeishuAdaptor 的并发读写安全)。前端为静态 HTML + 原生 JS,无构建步骤。

```
浏览器 ──HTTP──> Kestrel(Minimal API + Cookie 鉴权)
                     │
                     ├── wwwroot/(index.html / login.html / app.js / marked.js)
                     └── /api/* → ChatHistoryQueries → IDbContextFactory<ManInBlackDbContext>
                                                            │
                                                            └── 只读 SQLite 文件
```

---

## 4. 项目结构

```
demo/ChatViewer/
  ChatViewer.csproj            # Microsoft.NET.Sdk.Web, net10.0
  Program.cs                   # WebApplication 宿主 + DI + 端点 + 鉴权装配
  Auth/
    ChatViewerOptions.cs       # { Password } 配置绑定
    AuthEndpoints.cs           # cookie 鉴权中间件 + /login 流程
  Data/
    ChatHistoryQueries.cs      # 直接查 DbContext:会话列表/用户/搜索/单会话
    ReadModels.cs              # SessionSummary / UserSummary / MessageView / SearchResult
    ChatMessageRenderer.cs     # ChatMessage → MessageView(纯映射,无 DB)
  wwwroot/
    login.html                 # 登录页(匿名)
    index.html                 # 应用主体(需登录)
    app.js                     # 原生 JS
    styles.css
    lib/marked.js              # vendored,不走 CDN
  Properties/launchSettings.json
```

---

## 5. 宿主与 DI

**关键决定:不调用完整的 `AddManInBlack()`。**

`AddManInBlack()` 会注册整套 Agent 运行时(`IChatClient`、Provider/ApiKey、MCP、工具、`AgentFactory` 等),查看器完全不需要,且可能因未配 Provider 而启动失败。查看器唯一依赖核心库的部分是 `IDbContextFactory<ManInBlackDbContext>` + `AgentStorageOptions.RootPath`。

因此查看器在 `Program.cs` 自行:

1. `builder.Configuration.AddManInBlackSettings();`(复用 `~/.man-in-black/settings.json` 配置源,与 FeishuAdaptor 一致)
2. `services.Configure<AgentStorageOptions>(builder.Configuration.GetSection("Storage"));`(缺省 `~/.man-in-black`),使 `IOptions<AgentStorageOptions>` 可解析
3. 直接注册 DbContextFactory(**只读连接串 + 不加迁移拦截器**):

```csharp
builder.Services.AddDbContextFactory<ManInBlackDbContext>((sp, o) =>
{
    var root = sp.GetRequiredService<IOptions<AgentStorageOptions>>().Value.RootPath;
    o.UseSqlite($"Data Source={Path.Combine(root, "maninblack.db")};Mode=ReadOnly");
});
```

4. **不调用** `MigrateManInBlackStorageAsync()`——建表/迁移是写入方(FeishuAdaptor)的职责。
5. 所有查询 `AsNoTracking()`。

---

## 6. 数据访问(`Data/ChatHistoryQueries.cs`)

直查 DbContext,核心库抽象(`ISessionStorage`/`IUserStorage`)**保持不动**(它们的契约本就是「Agent 运行期写/加载」,不该为查看器塞枚举方法)。

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

- `AddAuthentication(CookieDefaults.AuthenticationScheme).AddCookie(...)`,cookie 路径 `/`,登录页 `/login`
- 配置:`settings.json` 新增节 `ChatViewer: { Password }`(与 `Feishu` 节并列,同为 settings.json 敏感项)
- **Fail-closed**:`Password` 未配置或为空 → 启动抛异常退出,绝不暴露无密码的查看器
- 密码比对用 `CryptographicOperations.FixedTimeEquals`(固定时长,防计时侧信道);明文存储与现有 AppSecret 模式一致(可选:未来支持预哈希值)
- 流程:`GET /login`(匿名)→ `POST /api/login { password }` 校验 → `SignIn` 发 cookie → 跳转 `/`;密码错返 401 可重试
- `POST /api/logout` 清除
- 其余路由 `RequireAuthorization()`;页面未登录跳 `/login`,`/api/*` 未登录返 401

---

## 8. API 端点

| 方法 路径 | 鉴权 | 返回 |
|---|---|---|
| `POST /api/login` | 匿名 | 设置 cookie |
| `POST /api/logout` | 需登录 | 清除 cookie |
| `GET /api/sessions` | 需登录 | `SessionSummary[]` |
| `GET /api/sessions/{id}/messages` | 需登录 | `MessageView[]` |
| `GET /api/users` | 需登录 | `UserSummary[]` |
| `GET /api/search?q=` | 需登录 | `SearchResult[]` |
| 静态文件 `/` | `/login` 匿名,其余需登录 | `wwwroot` 下 `index.html` / `login.html` |

---

## 9. 前端(原生 JS)

**布局**:
```
┌──────────────────────────────────────────────────────────┐
│ ManInBlack · 聊天记录查看器                  [退出]      │ 顶栏
├───────────────┬─────────────────────────────────────────┤
│[会话][用户][搜索]│ 会话 abc123 · 12 条 · 用户 u_42          │
│ 会话列表       │  [user] 你好                              │
│ abc123   12条  │  [assistant] 你好!有什么可以帮你?        │
│ def456    3条  │     ▸ 工具调用 read_file(path=…)    [▾]  │
│ …             │     ▸ 工具结果 {…}                 [▾]  │
└───────────────┴─────────────────────────────────────────┘
   左 sidebar           右 主消息区
```

- **左 sidebar**(顶部 tab 切换):
  - **会话**:`SessionSummary` 列表(id 缩写 + 消息数 + 末次时间),点击 → 主区加载消息
  - **用户**:`UserSummary` 列表(UserId + 会话数),展开 → 名下会话,点会话 → 加载消息
  - **搜索**:输入框 → 命中列表(会话 id + 片段 + 时间),点击 → 跳转并高亮命中
- **右主消息区**:头部(会话 id 缩写 + 消息数 + 关联用户)+ 逐条消息(role 徽章 + 内容块);默认滚到最底
- 内容块渲染:文本 → `marked.js` markdown;工具调用/结果 → 可折叠卡片;未知 → 灰色 `<details>` raw JSON
- **安全**:工具 JSON 用 `JSON.stringify` + `textContent` 插入(不走 `innerHTML`),杜绝 XSS;文本块走 marked(查看自有 Agent 日志,半可信)
- **依赖**:`marked.js` 单文件 vendor 到 `wwwroot/lib/`(不走 CDN,内网部署可能无外网);零构建步骤

---

## 10. 错误处理

- **单行 JSON 损坏**:跳过该行 + 日志告警,不崩溃整个列表
- **DB 不存在/不可读**:只读连接首次查询抛 → 端点 catch 返 503 + 清晰信息,UI 顶部显示「无法读取数据库」横幅(团队部署下 DB 由 FeishuAdaptor 创建,正常不触发)
- **搜索**:空查询 → 空结果;LIKE 走 EF 参数化,无注入风险
- **鉴权**:密码错 → 401 可重试

---

## 11. 测试(`test/ChatViewer.Tests`,xunit + 手写 fake)

- `ChatMessageRendererTests`(纯逻辑,无 DB):构造含 `TextContent`/`FunctionCallContent`/`FunctionResultContent`/未知类型的 `ChatMessage`,断言 `MessageView` 块正确
- `ChatHistoryQueriesTests`(临时 SQLite 文件):种入正常行 + 一行损坏 JSON,断言会话分组摘要、用户列表与会话映射、搜索 LIKE 命中、损坏行跳过不崩
- `AuthTests`:密码正确/错误比对、`Password` 为空时 fail-closed(启动抛异常)
- 端点是薄包装,主覆盖在查询+映射层;必要时补 1-2 个 `WebApplicationFactory` 冒烟测试

---

## 12. 文档与部署

**文档(CLAUDE.md 约定:改模块同步更新 docs)**:
- 新增 `docs/chatviewer-guide.md`(用途、配置、运行、部署、安全说明)
- 更新 `CLAUDE.md`:加构建/运行命令 + 文档索引条目
- `docs/storage-guide.md` 加一行指向 ChatViewer(可选)

**构建/运行**:
```bash
dotnet run --project demo/ChatViewer     # 默认 http://localhost:5080(launchSettings)
```

**部署**:沿用 FeishuAdaptor 的 `publish linux-x64` + systemd + 反向代理模式;应用层密码之外可再叠一层代理 basic auth。细节落到 `chatviewer-guide.md`。

---

## 13. 配置示例(settings.json 新增节)

```json
{
  "Storage": { "RootPath": "~/.man-in-black" },
  "ChatViewer": { "Password": "a-long-random-shared-secret" }
}
```

---

## 14. 未来增强(明确不在本期)

- FTS5 全文索引(替 LIKE,提速中文搜索)
- 状态快照查看(`AgentStateSnapshots`)
- 会话导出(Markdown/JSON)
- 写操作(删除/清理旧会话)
- 预哈希密码、OIDC/SSO
