# GitHub App Code Reviewer 设计文档

## 概述

基于 ManInBlack AI 框架构建 GitHub App，自动监听仓库 PR 事件，触发 AI agent 进行 code review，以行内评论 + PR 总结的形式发布审查结果。部署方式为 ASP.NET Core Web API 容器化部署。

## GitHub App 注册

在 GitHub Settings → Developer settings → GitHub Apps 创建应用：

**Webhook 配置：**
- URL: `https://{domain}/github/webhook`
- Content-Type: `application/json`
- Secret: 自定义，用于 HMAC-SHA256 签名验证

**权限申请：**

| 权限 | 级别 | 用途 |
|------|------|------|
| Pull requests | Read & Write | 读取 PR diff，提交 review |
| Contents | Read | 按需读取完整文件内容 |
| Commit statuses | Read | 读取 PR 状态 |
| Metadata | Read | 基本仓库信息（默认） |

**订阅事件：**
- `pull_request`（opened, synchronize）
- `installation`（安装/卸载）

**Review 提交方式：**
使用 `POST /repos/{owner}/{repo}/pulls/{number}/reviews` 提交 review，包含行内评论 + body 总结，附带 `APPROVE` / `REQUEST_CHANGES` / `COMMENT` 事件类型。

## 项目结构

在 `demo/GitHubAdaptor/` 下新建 ASP.NET Core Web API 项目，沿用 FeishuAdaptor 的架构模式：

```
demo/GitHubAdaptor/
├── GitHubAdaptor.csproj
├── Program.cs                          # ASP.NET Core 入口，DI 注册 + 端点映射
├── Dockerfile                          # 基于 dotnet new 生成，加装 gh CLI
├── appsettings.json                    # GitHub App 配置（AppId, PrivateKeyPath, WebhookSecret）
├── appsettings.Development.json        # 本地开发配置
│
├── Webhook/
│   ├── GitHubWebhookMiddleware.cs      # HMAC-SHA256 签名验证中间件
│   └── GitHubEventDispatcher.cs        # 事件反序列化 + 路由到对应 Handler
│
├── Handlers/
│   └── PullRequestHandler.cs           # 处理 opened/synchronize → 触发 review
│
├── Services/
│   ├── GitHubAppTokenService.cs        # JWT 生成 → installation token 换取 + 缓存
│   └── GitHubCliSetup.cs              # 用 token 配置 gh CLI 认证（gh auth login --with-token）
│
├── Models/
│   └── GitHubWebhookPayload.cs         # Webhook payload 模型
│
└── Health/
    └── HealthChecks.cs                 # GET /health 健康检查端点
```

**设计决策：不写自定义 AiTool，用 `gh` CLI 替代。** ManInBlack 已有 `CommandLineToolsMiddleware`，agent 通过 `gh` 命令完成所有 GitHub 操作（读取 diff、获取文件内容、提交 review），无需编写额外的工具类。

## Agent 配置

在 `~/.man-in-black/settings.json` 中新增 `github-reviewer` agent：

```json
{
  "Agents": {
    "github-reviewer": {
      "Instruction": "你是一个专业的代码审查员。审查 PR diff 中的代码变更，识别 bug、安全漏洞、性能问题和可维护性隐患。对不确定的上下文，使用 gh api 读取相关文件全文。最后使用 gh api 提交 review，包含精确到行的行内评论和 PR 级总结。",
      "PipelineName": "default",
      "ModelChoice": "default"
    }
  }
}
```

Agent 复用现有 Provider 配置（OpenAI/Anthropic/Gemini），不单独配置模型。

## Review 流程

```
GitHub PR Event (opened / synchronize)
    │
    ▼
GitHubWebhookMiddleware
    │  验证 X-Hub-Signature-256（HMAC-SHA256）
    ▼
GitHubEventDispatcher
    │  过滤：仅处理 pull_request 事件，action = opened | synchronize
    │  提取：installation_id, repository, pr_number
    ▼
PullRequestHandler
    │
    ├── 1. GitHubAppTokenService.GetInstallationTokenAsync(installationId)
    │       用 App 私钥生成 JWT →换取 installation token（缓存，1小时刷新）
    │
    ├── 2. GitHubCliSetup.ConfigureAsync(token)
    │       echo $token | gh auth login --with-token
    │
    ├── 3. 获取 diff
    │       gh pr diff {number}
    │
    ├── 4. 构建 AgentContext 并运行
    │       SystemPrompt = agent instruction + 仓库/PR 元信息
    │       UserInput = diff 内容 + PR title/description
    │       AgentFactory.RunAsync("github-reviewer", context)
    │       │
    │       Agent 自主执行（通过 CommandLineTools）：
    │       ├── 分析 diff，识别问题
    │       ├── gh api repos/{owner}/{repo}/contents/{path}  按需读取完整文件
    │       ├── gh api .../pulls/{number}/reviews             提交 review
    │       └── 输出审查完成
    │
    └── 5. gh auth logout  清理凭证
```

**并发控制：** 每个 PR review 是独立 agent session，用 `installation_id + pr_number` 做 session 隔离，同一仓库多个 PR 可并发处理。

## 配置加载

不使用额外环境变量指定配置路径，统一使用默认路径 `~/.man-in-black/settings.json`：

**本地开发：** 直接读取本机 `~/.man-in-black/settings.json`，复用现有 Provider 和 Agent 配置。

**Docker 部署：** 通过 volume 挂载到容器内相同路径：

```bash
docker run \
  -v /path/to/settings.json:/root/.man-in-black/settings.json \
  -v /path/to/private-key.pem:/config/private-key.pem \
  -e GITHUB_APP_ID=123456 \
  -e GITHUB_WEBHOOK_SECRET=xxx \
  -p 8080:8080 \
  github-adaptor:latest
```

GitHub App 敏感配置（AppId、WebhookSecret）通过环境变量注入，在 `appsettings.json` 中用 `%ENV_VAR%` 占位或 ASP.NET Core 标准环境变量覆盖机制。

## Webhook 安全性

- **签名验证：** HMAC-SHA256，比对 `X-Hub-Signature-256` header，不匹配返回 401
- **事件过滤：** 只处理 `pull_request` 的 `opened` 和 `synchronize` action，其余返回 200 直接丢弃
- **凭证清理：** 每次 review 完成后执行 `gh auth logout`
- **私钥保护：** 私钥文件权限 600，仅容器内可读

## 容器化

Dockerfile 基于 `dotnet new` 自动生成的标准多阶段构建，唯一改动是在 final stage 加装 `gh` CLI：

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["demo/GitHubAdaptor/GitHubAdaptor.csproj", "demo/GitHubAdaptor/"]
RUN dotnet restore "demo/GitHubAdaptor/GitHubAdaptor.csproj"
COPY . .
RUN dotnet build "demo/GitHubAdaptor/GitHubAdaptor.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
RUN dotnet publish "demo/GitHubAdaptor/GitHubAdaptor.csproj" -c $BUILD_CONFIGURATION -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080

# 加装 gh CLI
RUN apt-get update && apt-get install -y gh && rm -rf /var/lib/apt/lists/*

COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "GitHubAdaptor.dll"]
```

健康检查端点 `GET /health` 用于容器编排探活。
