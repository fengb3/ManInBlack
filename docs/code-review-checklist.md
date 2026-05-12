# 代码审查待办清单

> 生成时间：2025-05-09 | 最后更新：2025-05-09
> 基于 9 个文档维度的全量代码审查结果，已完成的标记为 ✅，待修复的标记为 ⬜。
> 提交：67d2a74 🐛 修复 FileTools.Edit、SerializeTools、更新文档

---

## 一、高优先级（影响正确性/稳定性）

### 已完成

- [x] **H1: FileTools.Edit 行为 bug** — `string.Replace` 替换所有匹配，改为 `IndexOf` 只替换首次出现。`src/ManInBlack.AI/Tools/FileTools.cs`
- [x] **H2: Anthropic/Gemini SerializeTools 类型判断错误** — `is AIFunction` 改为 `is AIFunctionDeclaration`，修复 tool 参数 schema 丢失。`src/ManInBlack.AI/ChatClient/AnthropicCompatibleChatClient.cs`、`GeminiCompatibleChatClient.cs`
- [x] **H4: 快速开始文档示例无法编译** — 添加 EventBus.Subscribe 的 key 参数，更新输出方式为 ModelContentEvent。`docs/quick-start.md`

### 待修复

- [x] **H3: 文档与代码全面不一致** — 架构、中间件、工厂、事件总线、Hook 文档均已更新
  - [x] `docs/agent-factory-guide.md` — EventBus 生命周期、已删除中间件引用
  - [x] `docs/eventbus-guide.md` — 事件发布者记录
  - [x] `docs/hooks-guide.md` — 添加 EventBus 间接机制说明
  - [x] `docs/architecture.md` — 管道顺序、Filter 名称、中间件数量、Abstraction 目录结构
  - [x] `docs/middleware-guide.md` — 管道顺序（UseDefault 外层 + UseSimple 内层）、中间件名称
- [x] **H3-remaining: tools-guide.md 文档与代码不匹配** — 方法名已修正（ReadFile→Read, WriteFile→Write, UpdateFile→Edit），KillBackgroundTask 已补充，DeleteFile/DeleteDirectory 标注已注释。`docs/tools-guide.md`
- [x] **H5: 同步阻塞异步方法（死锁风险）** — 已修复
  - [x] `src/ManInBlack.AI/Middlewares/PersistenceMiddleware.cs` — `PersistingMessageCollection` 改用 `Channel<ChatMessage>` 异步消费，管道结束后 `FlushAsync`
  - [x] `src/ManInBlack.AI/Services/FileUserWorkspace.cs` — `_user` 改为 `Lazy<UserEntry>` 延迟初始化
- [x] **H6: 飞书卡片逻辑提取为独立类** — 从匿名回调重构为 `FeishuCardSession`。`demo/FeishuAdaptor/FeishuCard/FeishuCardSession.cs`
- [ ] **H7: InjectTarget 功能不完整** — 只实现了 SystemPrompt 注入，UserMessage 和 ToolResult 分支缺失。`src/ManInBlack.AI/Middlewares/HookMiddleware.cs:129-130`
- [ ] **H8: SystemPromptInjectionMiddleware 未处理 null** — `context.SystemPrompt.Length` 在 SystemPrompt 为 null 时 NRE。`src/ManInBlack.AI/Middlewares/SystemPromptInjectionMiddleware.cs:25`
- [ ] **H9: EventPublishingMiddleware 无测试** — 默认管道最外层中间件零覆盖。`test/ManInBlack.AI.Tests/`

---

## 二、中优先级（代码重复/架构问题/测试缺失）

### 代码重复/冗余

- [ ] **M1: 三个 ChatClient 适配器大量结构重复** — SSE 流解析、错误处理、ParseArguments、Dispose 空实现约 30-40 行相同代码。考虑提取共享基类或辅助方法。
  - `src/ManInBlack.AI/ChatClient/OpenAICompatibleChatClient.cs`
  - `src/ManInBlack.AI/ChatClient/AnthropicCompatibleChatClient.cs`
  - `src/ManInBlack.AI/ChatClient/GeminiCompatibleChatClient.cs`
- [ ] **M2: 源生成器三个 Generator 之间重复代码** — `UnwrapAsyncReturnType`、`IsTaskType`、`ResolveToolNames`、`ToolAttributeFullName` 常量各复制一份。应提取共享工具类。
  - `src/ManInBlack.AI.SourceGenerator/ToolCallerGenerator.cs`
  - `src/ManInBlack.AI.SourceGenerator/ToolDeclarationGenerator.cs`
  - `src/ManInBlack.AI.SourceGenerator/ToolMiddlewareGenerator.cs`
- [ ] **M3: 测试辅助方法重复** — `AgentLifecycleFilterTests` 和 `HookMiddlewareTests` 中 `BuildSp` 方法完全相同。提取到共享 TestHelpers。
- [ ] **M4: AgentLifecycleFilterTests 大量重复 setup 代码** — 几乎每个测试都内联完整 setup，Setup() 辅助方法未被使用。应重构测试基础设施。
- [ ] **M5: Gemini/Anthropic ChatClient 测试重复 Usage 测试** — 两个测试类有完全相同的 JSON 数据和断言。删除其中一个。
- [ ] **M6: CardUpdateScheduler 重复 API 调用代码** — `FlushAsync` 和 `ProcessLoopAsync` 有相同的限流+API 调用模式。`demo/FeishuAdaptor/FeishuCard/CardUpdateScheduler.cs`
- [ ] **M7: ToolExecutionCardView 重复面板构建代码** — `UpdateForToolStartAsync` 和 `UpdateForToolResultAsync` 手动设置相同属性。`demo/FeishuAdaptor/FeishuCard/CardViews/ResponseCardView.cs`
- [ ] **M8: ModelChoice 与 ModelChoiceSettings 字段重叠** — 运行时和配置类持有高度重叠字段，手动映射。应考虑统一或使用映射代码生成。
- [ ] **M9: Schema 合法值散布在 4+ 处文件硬编码** — `"OpenAI"/"Anthropic"/"Gemini"` 应定义为 enum 或集中常量。
  - `src/ManInBlack.AI/Configuration/ValidateManInBlackSettings.cs`
  - `src/ManInBlack.AI/Providers.cs`
  - `src/ManInBlack.AI/Configuration/ManInBlackSettings.cs`

### 架构/设计问题

- [ ] **M10: EventBus 静态存储无清理机制** — handler 列表按 key 隔离但无超时/弱引用清理，订阅泄漏会导致内存增长。`src/ManInBlack.AI/Services/EventBus.cs`
- [ ] **M11: Hook 系统和 EventBus 双轨制** — 两条路径执行顺序不确定，需要优先级保证的场景（如安全检查）有风险。
- [ ] **M12: AgentLifecycleFilter CancellationToken 硬编码 default** — 工具执行前后的 EventBus 发布无法取消。`src/ManInBlack.AI/ToolCallFilters/AgentLifecycleFilter.cs:39,54`
- [ ] **M13: ManInBlackSettings 双轨注册** — 同时注册 IOptions 和直接单例，访问方式不一致。`src/ManInBlack.AI/DependencyInjection.cs`
- [ ] **M14: FeishuAdaptor 重复调用 AddAutoRegisteredServices()** — `AddManInBlackFromConfiguration` 内部已调用，外部又调一次。`demo/FeishuAdaptor/Program.cs:72`
- [ ] **M15: SlidingWindowRateLimiter 竞态条件** — `GetRequiredDelay` 和 `RecordCall` 之间存在时间窗口。`demo/FeishuAdaptor/FeishuCard/SlidingWindowRateLimiter.cs`
- [ ] **M16: AgentLoopMiddleware 残留 Console 颜色操作** — 库代码不应操控宿主控制台。`src/ManInBlack.AI/Middlewares/AgentLoopMiddleware.cs:96-99`
- [ ] **M17: HookSettings.Script XML 注释错误** — 写"脚本路径"但实际是原始 shell 命令。`src/ManInBlack.AI/Configuration/HookSettings.cs:15`
- [ ] **M18: HookMiddleware 忽略 HookResult 返回值** — AfterLlmCall/AfterToolExecute/AllToolsCompleted/AgentCompleted 的 handler 不检查 Succeeded。`src/ManInBlack.AI/Middlewares/HookMiddleware.cs:52-117`

### 测试缺失

- [ ] **M19: 无配置/Provider 单元测试** — ValidateManInBlackSettings、SettingsLoader 等无覆盖。
- [ ] **M20: HookExecutor 无测试** — 钩子匹配、短路逻辑、多钩子结果合并未覆盖。`src/ManInBlack.AI/Services/HookExecutor.cs`
- [ ] **M21: AgentPipelineBuilder 无测试** — 管道构建顺序、Use<T>() DI 解析未覆盖。`src/ManInBlack.AI/Middlewares/AgentPipelineBuilder.cs`
- [ ] **M22: AgentProfileMiddleware 无测试** — profile.md 读取和注入逻辑未覆盖。
- [ ] **M23: 飞书核心组件无测试** — ImMessageReceiveEventHandler、AgentLauncher、CardView 无覆盖。
- [ ] **M24: FileToolsTests 大量注释掉的测试** — 含路径遍历安全测试，应删除或恢复。`test/ManInBlack.AI.Tests/Tools/FileToolsTests.cs`

### 注释代码/死代码

- [ ] **M25: CommandToolMiddleware.cs 和 FileToolMiddleware.cs 整文件被注释** — 但 `AgentPipelines.cs` 仍引用生成的版本。应删除这些注释文件。
- [ ] **M26: 多处残留 Console.WriteLine 调试代码** — `SystemPromptInjectionMiddleware.cs:20-23,40-43`、`ContextCompressMiddleware.cs:44-47`、`OpenAICompatibleChatClient.cs:62-64,88-91`
- [ ] **M27: AgentPipelineBuilder.Build() 残留大量注释代码** — `AgentPipelineBuilder.cs:19-21,44-46,63-64`

---

## 三、低优先级（代码整洁/约定/命名）

### 英文注释违反约定（应改为中文）

- [ ] `src/ManInBlack.AI/Tools/CommandLineTools.cs:174-178` — CheckDangerousCommand 方法 XML 注释全英文
- [ ] `demo/FeishuAdaptor/Program.cs` — 行 1, 46-48, 93 英文注释
- [ ] `src/ManInBlack.AI/Services/UserInputCommandHelper.cs:17` — `// a command should start with '/'`
- [ ] `demo/FeishuAdaptor/Helper/StringHelper.cs` — 多处英文 XML 注释

### 死代码（完全未使用）

- [ ] `demo/FeishuAdaptor/Helper/` 下 14 个 Helper 文件无引用，应清理删除
  - `ArrayHelper.cs`, `DatabaseHelper.cs`, `DelegateHelper.cs`, `EnumHelper.cs`, `FileHelper.cs`, `FeishuHelper.cs`, `GUIDHelper.cs`, `LoopHelper.cs`, `ObjectHelper.cs`, `StreamHelper.cs`, `TaskHelper.cs`, `ThrowHelper.cs`, `TimeHelper.cs`, `StringHelper.cs`（部分方法未使用）
- [ ] `src/ManInBlack.AI.Abstraction/IModelProvider.cs` — 空文件，仅含"已移除"注释
- [ ] `demo/FeishuAdaptor/EventHandlers/ImMessageReadEventHandler.cs` — 空实现+注释掉的代码

### 变量拼写错误

- [ ] `src/ManInBlack.AI/Middlewares/ContextCompressMiddleware.cs:18-19` — `keeped` → `kept`, `transed` → `transformed`
- [ ] `src/ManInBlack.AI/ChatClient/GeminiCompatibleChatClient.cs:17` — `_blockedEndpoint` 应为 `_baseEndpoint`
- [ ] `demo/FeishuAdaptor/Helper/FeishuHelper.cs:17` — `"unkonwn"` → `"unknown"`

### 命名不一致

- [ ] `test/ManInBlack.AI.Tests/Helpers/MockHttpMessageHandler.cs` — 实际是手写 fake，应重命名为 `FakeHttpMessageHandler`
- [ ] `src/ManInBlack.AI.SourceGenerator/ToolMethodModel.cs:6,24` — XML 注释写 `[Tool]` 应为 `[AiTool]`

### 源生成器

- [ ] `src/ManInBlack.AI.SourceGenerator/ToolDeclarationEmitter.cs:124,179` — 使用原始 `StringBuilder`，违反项目约定
- [ ] `src/ManInBlack.AI.SourceGenerator/ToolDeclarationEmitter.cs:256-264` — `EscapeString` 和 `EscapeJsonString` 实现完全相同，应合并
- [ ] `src/ManInBlack.AI.SourceGenerator/ToolMethodModel.cs:29-30` — `FullTypeName` 和 `Type` 被赋相同值，`FullTypeName` 是死属性
- [ ] `src/ManInBlack.AI.SourceGenerator/ToolMethodModel.cs:13` — `FullyQualifiedTypeName` 未被使用

### Demo 问题

- [ ] `demo/Playground/Program.cs` — 几乎所有代码被注释掉，仅打印 SpecialFolder 路径，无参考价值
- [ ] `demo/AgentConsole/Program.cs:30` — `args[0]` 无空检查，直接运行会 IndexOutOfRange
- [ ] `demo/FeishuAdaptor/.env.example` — 环境变量与实际代码使用方式不匹配

### 其他

- [ ] `src/ManInBlack.AI/Middlewares/RetryMiddleware.cs:72` — 错误提示英文硬编码，与 LoggerMessage 中文日志不一致
- [ ] `src/ManInBlack.AI/Middlewares/AgentProfileMiddleware.cs:62` — 硬编码英文提示词，应考虑可配置
- [ ] `src/ManInBlack.AI/DependencyInjection.cs:38` — `AgentPipelineBuilder` 冗余 DI 注册，代码中直接 `new`
- [ ] `src/ManInBlack.AI/AgentFactory.cs:72` — `Definitions` 属性 `(IReadOnlyCollection<AgentDefinition>)` 强制转换不直观
- [ ] `src/ManInBlack.AI/Tools/FileTools.cs` Grep 方法 — 缺少 `.git`/`node_modules`/`bin`/`obj` 目录过滤，大项目性能差
- [ ] `src/ManInBlack.AI/Tools/FileTools.cs` Grep 的 glob 参数 — 使用旧式通配符而非真正 glob（同文件 Glob 方法使用 Matcher）
- [ ] `demo/FeishuAdaptor/FeishuCard/CardApiLimiter.cs` — 所有接口共享相同限流参数，无注释说明是否与飞书实际限制匹配
- [ ] `test/ManInBlack.AI.Tests/Middlewares/SkillMiddlewareTests.cs:50-57` — 测试依赖开发环境，不同环境行为不一致
- [ ] `test/ManInBlack.AI.IntegrationTests/` — 空壳项目，无实际测试
