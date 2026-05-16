# GitHub App Code Reviewer 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 构建 GitHub App，自动监听 PR 事件并使用 ManInBlack AI agent 进行 code review，以行内评论 + PR 总结形式发布结果。

**Architecture:** ASP.NET Core Web API 接收 GitHub Webhook，验证 HMAC-SHA256 签名后换取 installation token，配置 gh CLI 认证，启动 ManInBlack agent。Agent 通过 CommandLineTools 调用 gh API 读取 diff/文件、提交 review。

**Tech Stack:** .NET 10, ASP.NET Core, ManInBlack.AI, gh CLI, System.Security.Cryptography

---

## File Structure

| File | Responsibility |
|------|---------------|
| `demo/GitHubAdaptor/GitHubAdaptor.csproj` | 项目定义，引用 ManInBlack.AI + SourceGenerator |
| `demo/GitHubAdaptor/Program.cs` | ASP.NET Core 入口，DI 注册 + 端点映射 |
| `demo/GitHubAdaptor/appsettings.json` | 基础配置（日志等） |
| `demo/GitHubAdaptor/appsettings.Development.json` | 本地开发配置 |
| `demo/GitHubAdaptor/Dockerfile` | 容器构建，基于 dotnet new 模板 + gh CLI |
| `demo/GitHubAdaptor/Models/GitHubSettings.cs` | GitHub App 配置模型（AppId, PrivateKeyPath, WebhookSecret） |
| `demo/GitHubAdaptor/Models/PullRequestPayload.cs` | Webhook payload 反序列化模型 |
| `demo/GitHubAdaptor/Services/GitHubAppTokenService.cs` | JWT 生成 + installation token 换取 + 缓存 |
| `demo/GitHubAdaptor/Services/GitHubCliSetup.cs` | gh auth login/logout + gh 命令执行 |
| `demo/GitHubAdaptor/Webhook/GitHubWebhookMiddleware.cs` | HMAC-SHA256 签名验证 ASP.NET Core 中间件 |
| `demo/GitHubAdaptor/Webhook/GitHubEventDispatcher.cs` | 事件类型路由 → 调用 PullRequestHandler |
| `demo/GitHubAdaptor/Handlers/PullRequestHandler.cs` | PR review 编排：获取 token → 配置 gh → 获取 diff → 运行 agent |

---

### Task 1: 创建项目骨架

**Files:**
- Create: `demo/GitHubAdaptor/GitHubAdaptor.csproj`
- Create: `demo/GitHubAdaptor/GlobalUsings.cs`
- Create: 目录结构

- [ ] **Step 1: 创建项目**

Run:
```bash
cd /c/Users/fohhy/source/repos/ManInBlack
dotnet new webapi -n GitHubAdaptor -o demo/GitHubAdaptor --no-https
```

- [ ] **Step 2: 修改 csproj，添加 ManInBlack 引用**

替换 `demo/GitHubAdaptor/GitHubAdaptor.csproj` 内容：

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Serilog.AspNetCore" Version="10.0.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\ManInBlack.AI\ManInBlack.AI.csproj" />
    <ProjectReference Include="..\..\src\ManInBlack.AI.SourceGenerator\ManInBlack.AI.SourceGenerator.csproj" OutputItemType="Analyzer" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: 创建目录结构和 GlobalUsings**

Run:
```bash
mkdir -p demo/GitHubAdaptor/Models
mkdir -p demo/GitHubAdaptor/Services
mkdir -p demo/GitHubAdaptor/Webhook
mkdir -p demo/GitHubAdaptor/Handlers
```

创建 `demo/GitHubAdaptor/GlobalUsings.cs`：

```csharp
global using System.Security.Cryptography;
global using System.Text;
global using System.Text.Json;
global using System.Text.Json.Serialization;
```

- [ ] **Step 4: 删除 dotnet new 自动生成的多余文件**

Run:
```bash
rm -f demo/GitHubAdaptor/Controllers/*.cs
rm -f demo/GitHubAdaptor/appsettings.json
rm -f demo/GitHubAdaptor/appsettings.Development.json
```

- [ ] **Step 5: 验证构建**

Run:
```bash
dotnet build demo/GitHubAdaptor
```
Expected: 构建成功（可能有 Program.cs 相关错误，后续步骤修复）

- [ ] **Step 6: 提交**

```bash
git add demo/GitHubAdaptor/
git commit -m "🏗️ 创建 GitHubAdaptor 项目骨架"
```

---

### Task 2: 定义配置与 Payload 模型

**Files:**
- Create: `demo/GitHubAdaptor/Models/GitHubSettings.cs`
- Create: `demo/GitHubAdaptor/Models/PullRequestPayload.cs`

- [ ] **Step 1: 创建 GitHubSettings**

创建 `demo/GitHubAdaptor/Models/GitHubSettings.cs`：

```csharp
namespace GitHubAdaptor.Models;

public class GitHubSettings
{
    public long AppId { get; set; }
    public string PrivateKeyPath { get; set; } = "";
    public string WebhookSecret { get; set; } = "";
    public string WebhookEndpoint { get; set; } = "/github/webhook";
}
```

- [ ] **Step 2: 创建 PullRequestPayload**

创建 `demo/GitHubAdaptor/Models/PullRequestPayload.cs`：

```csharp
namespace GitHubAdaptor.Models;

public class PullRequestPayload
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = "";

    [JsonPropertyName("number")]
    public int Number { get; set; }

    [JsonPropertyName("pull_request")]
    public PullRequest? PullRequest { get; set; }

    [JsonPropertyName("repository")]
    public Repository? Repository { get; set; }

    [JsonPropertyName("installation")]
    public Installation? Installation { get; set; }
}

public class PullRequest
{
    [JsonPropertyName("number")]
    public int Number { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = "";

    [JsonPropertyName("base")]
    public PullRequestBranch? Base { get; set; }

    [JsonPropertyName("head")]
    public PullRequestBranch? Head { get; set; }
}

public class PullRequestBranch
{
    [JsonPropertyName("ref")]
    public string Ref { get; set; } = "";

    [JsonPropertyName("sha")]
    public string Sha { get; set; } = ""
    ;
}

public class Repository
{
    [JsonPropertyName("full_name")]
    public string FullName { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("owner")]
    public RepositoryOwner? Owner { get; set; }
}

public class RepositoryOwner
{
    [JsonPropertyName("login")]
    public string Login { get; set; } = "";
}

public class Installation
{
    [JsonPropertyName("id")]
    public long Id { get; set; }
}
```

- [ ] **Step 3: 验证构建**

Run:
```bash
dotnet build demo/GitHubAdaptor
```
Expected: 构建成功

- [ ] **Step 4: 提交**

```bash
git add demo/GitHubAdaptor/Models/
git commit -m "🏷️ 添加 GitHub 配置和 Webhook payload 模型"
```

---

### Task 3: 实现 GitHubAppTokenService

**Files:**
- Create: `demo/GitHubAdaptor/Services/GitHubAppTokenService.cs`

核心服务：用 App 私钥生成 JWT → 换取 installation token（缓存 1 小时）。

- [ ] **Step 1: 实现 GitHubAppTokenService**

创建 `demo/GitHubAdaptor/Services/GitHubAppTokenService.cs`：

```csharp
using ManInBlack.AI.Abstraction;
using Microsoft.Extensions.Logging;

namespace GitHubAdaptor.Services;

[ServiceRegister.Singleton]
public class GitHubAppTokenService(
    GitHubSettings settings,
    IHttpClientFactory httpClientFactory,
    ILogger<GitHubAppTokenService> logger)
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _cachedToken;
    private long _cachedInstallationId;
    private DateTime _tokenExpiresAt = DateTime.MinValue;

    public async Task<string> GetInstallationTokenAsync(long installationId, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_cachedToken != null && _cachedInstallationId == installationId && DateTime.UtcNow < _tokenExpiresAt)
                return _cachedToken;

            var jwt = GenerateJwt();
            var token = await ExchangeInstallationTokenAsync(jwt, installationId, ct);

            _cachedToken = token;
            _cachedInstallationId = installationId;
            _tokenExpiresAt = DateTime.UtcNow.AddMinutes(55); // token 有效期 1 小时，提前 5 分钟刷新

            logger.LogInformation("获取 installation token 成功，installation_id: {InstallationId}", installationId);
            return token;
        }
        finally
        {
            _lock.Release();
        }
    }

    private string GenerateJwt()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var payload = $"{{\"iat\":{now - 60},\"exp\":{now + 600},\"iss\":\"{settings.AppId}\"}}";
        var header = "{\"alg\":\"RS256\",\"typ\":\"JWT\"}";

        var headerBytes = Encoding.UTF8.GetBytes(header);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var message = $"{Base64UrlUrlEncode(headerBytes)}.{Base64UrlUrlEncode(payloadBytes)}";

        var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(settings.PrivateKeyPath));

        var signature = rsa.SignData(
            Encoding.UTF8.GetBytes(message),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return $"{message}.{Base64UrlUrlEncode(signature)}";
    }

    private async Task<string> ExchangeInstallationTokenAsync(string jwt, long installationId, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwt);
        client.DefaultRequestHeaders.Accept.Add(new("application/vnd.github+json"));
        client.DefaultRequestHeaders.UserAgent.Add(new("GitHubAdaptor", "1.0"));

        var response = await client.PostAsync(
            $"https://api.github.com/app/installations/{installationId}/access_tokens",
            null, ct);

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("token").GetString()
            ?? throw new InvalidOperationException("GitHub API 未返回 token");
    }

    private static string Base64UrlUrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}
```

- [ ] **Step 2: 验证构建**

Run:
```bash
dotnet build demo/GitHubAdaptor
```
Expected: 构建成功

- [ ] **Step 3: 提交**

```bash
git add demo/GitHubAdaptor/Services/GitHubAppTokenService.cs
git commit -m "🔐 实现 GitHubAppTokenService：JWT 生成与 installation token 换取"
```

---

### Task 4: 实现 GitHubCliSetup

**Files:**
- Create: `demo/GitHubAdaptor/Services/GitHubCliSetup.cs`

封装 `gh auth login --with-token` 和 `gh auth logout`，以及通用的 gh 命令执行。

- [ ] **Step 1: 实现 GitHubCliSetup**

创建 `demo/GitHubAdaptor/Services/GitHubCliSetup.cs`：

```csharp
using System.Diagnostics;
using ManInBlack.AI.Abstraction;
using Microsoft.Extensions.Logging;

namespace GitHubAdaptor.Services;

[ServiceRegister.Singleton]
public class GitHubCliSetup(ILogger<GitHubCliSetup> logger)
{
    public async Task LoginAsync(string token, CancellationToken ct = default)
    {
        await RunProcessAsync($"auth login --with-token", token, ct);
        logger.LogInformation("gh CLI 认证成功");
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        try
        {
            await RunProcessAsync("auth logout", input: null, ct);
            logger.LogInformation("gh CLI 登出成功");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "gh CLI 登出失败，忽略");
        }
    }

    public async Task<string> RunGhAsync(string args, CancellationToken ct = default)
    {
        return await RunProcessAsync(args, input: null, ct);
    }

    private async Task<string> RunProcessAsync(string args, string? input, CancellationToken ct)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "gh",
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = input != null,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        process.Start();

        if (input != null)
        {
            await process.StandardInput.WriteAsync(input);
            process.StandardInput.Close();
        }

        var stdout = await process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            var stderr = await process.StandardError.ReadToEndAsync(ct);
            throw new InvalidOperationException($"gh {args} 失败 (exit {process.ExitCode}): {stderr}");
        }

        return stdout;
    }
}
```

- [ ] **Step 2: 验证构建**

Run:
```bash
dotnet build demo/GitHubAdaptor
```
Expected: 构建成功

- [ ] **Step 3: 提交**

```bash
git add demo/GitHubAdaptor/Services/GitHubCliSetup.cs
git commit -m "🔧 实现 GitHubCliSetup：gh CLI 认证与命令执行"
```

---

### Task 5: 实现 Webhook 处理

**Files:**
- Create: `demo/GitHubAdaptor/Webhook/GitHubWebhookMiddleware.cs`
- Create: `demo/GitHubAdaptor/Webhook/GitHubEventDispatcher.cs`

- [ ] **Step 1: 实现 GitHubWebhookMiddleware**

创建 `demo/GitHubAdaptor/Webhook/GitHubWebhookMiddleware.cs`：

```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace GitHubAdaptor.Webhook;

public class GitHubWebhookMiddleware(
    RequestDelegate next,
    GitHubSettings settings,
    ILogger<GitHubWebhookMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments(settings.WebhookEndpoint, StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        context.Request.EnableBuffering();
        using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        context.Request.Body.Position = 0;

        var signatureHeader = context.Request.Headers["X-Hub-Signature-256"].FirstOrDefault();
        if (string.IsNullOrEmpty(signatureHeader) || !VerifySignature(body, signatureHeader))
        {
            logger.LogWarning("Webhook 签名验证失败");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Invalid signature");
            return;
        }

        context.Items["RawBody"] = body;
        await next(context);
    }

    private bool VerifySignature(string body, string signatureHeader)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(settings.WebhookSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        var expected = $"sha256={Convert.ToHexStringLower(hash)}";
        return string.Equals(signatureHeader, expected, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 2: 实现 GitHubEventDispatcher**

创建 `demo/GitHubAdaptor/Webhook/GitHubEventDispatcher.cs`：

```csharp
using GitHubAdaptor.Handlers;
using GitHubAdaptor.Models;
using ManInBlack.AI.Abstraction;
using Microsoft.Extensions.Logging;

namespace GitHubAdaptor.Webhook;

[ServiceRegister.Singleton]
public class GitHubEventDispatcher(
    PullRequestHandler handler,
    ILogger<GitHubEventDispatcher> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task DispatchAsync(string eventType, string body, CancellationToken ct = default)
    {
        logger.LogInformation("收到 GitHub 事件: {EventType}", eventType);

        if (eventType != "pull_request")
        {
            logger.LogDebug("忽略非 PR 事件: {EventType}", eventType);
            return;
        }

        var payload = JsonSerializer.Deserialize<PullRequestPayload>(body, JsonOptions);
        if (payload is null)
        {
            logger.LogWarning("无法解析 pull_request payload");
            return;
        }

        if (payload.Action is not ("opened" or "synchronize"))
        {
            logger.LogDebug("忽略 PR action: {Action}", payload.Action);
            return;
        }

        logger.LogInformation("处理 PR #{Number} ({Action}) on {Repo}",
            payload.Number, payload.Action, payload.Repository?.FullName);

        await handler.HandleAsync(payload, ct);
    }
}
```

- [ ] **Step 3: 验证构建**

Run:
```bash
dotnet build demo/GitHubAdaptor
```
Expected: 构建成功

- [ ] **Step 4: 提交**

```bash
git add demo/GitHubAdaptor/Webhook/
git commit -m "🌐 实现 Webhook 签名验证与事件路由"
```

---

### Task 6: 实现 PullRequestHandler

**Files:**
- Create: `demo/GitHubAdaptor/Handlers/PullRequestHandler.cs`

核心编排：获取 token → 配置 gh → 获取 diff → 运行 agent → 清理。

- [ ] **Step 1: 实现 PullRequestHandler**

创建 `demo/GitHubAdaptor/Handlers/PullRequestHandler.cs`：

```csharp
using GitHubAdaptor.Models;
using GitHubAdaptor.Services;
using ManInBlack.AI;
using ManInBlack.AI.Abstraction;
using Microsoft.Extensions.Logging;

namespace GitHubAdaptor.Handlers;

[ServiceRegister.Singleton]
public class PullRequestHandler(
    GitHubAppTokenService tokenService,
    GitHubCliSetup cliSetup,
    AgentFactory agentFactory,
    ILogger<PullRequestHandler> logger)
{
    public async Task HandleAsync(PullRequestPayload payload, CancellationToken ct = default)
    {
        var installationId = payload.Installation?.Id
            ?? throw new InvalidOperationException("Payload 缺少 installation_id");

        var repo = payload.Repository?.FullName
            ?? throw new InvalidOperationException("Payload 缺少 repository");

        var prNumber = payload.Number;
        var prTitle = payload.PullRequest?.Title ?? "";
        var prBody = payload.PullRequest?.Body ?? "";
        var prUrl = payload.PullRequest?.HtmlUrl ?? "";

        logger.LogInformation("开始 review PR #{Number} on {Repo}", prNumber, repo);

        var token = await tokenService.GetInstallationTokenAsync(installationId, ct);

        await cliSetup.LoginAsync(token, ct);
        try
        {
            var diff = await cliSetup.RunGhAsync($"pr diff {prNumber} --repo {repo}", ct);

            logger.LogInformation("获取 diff 成功，长度: {Length}，启动 agent", diff.Length);

            var updates = agentFactory.RunAsync(
                "github-reviewer",
                diff,
                $"{installationId}-{prNumber}",
                "github_pr",
                ctx =>
                {
                    ctx.SystemPrompt += $"""

                        <github-context>
                        仓库: {repo}
                        PR: #{prNumber} - {prTitle}
                        PR 链接: {prUrl}
                        PR 描述: {prBody}
                        Base 分支: {payload.PullRequest?.Base?.Ref}
                        Head 分支: {payload.PullRequest?.Head?.Ref}

                        审查流程:
                        1. 分析 diff，识别潜在问题
                        2. 对不确定的上下文，用 `gh api repos/{repo}/contents/{{path}}` 读取完整文件
                        3. 用以下命令提交 review:
                           gh api repos/{repo}/pulls/{prNumber}/reviews --input - <<'REVIEW_EOF'
                           {{"body":"总结内容","event":"COMMENT","comments":[{{"path":"文件路径","position":行号,"body":"评论内容"}}]}}
                           REVIEW_EOF
                        4. 根据严重程度选择 event: APPROVE / REQUEST_CHANGES / COMMENT
                        5. position 是 diff 中的行号（从 1 开始），不是文件行号
                        </github-context>
                        """;
                },
                ct);

            await foreach (var _ in updates) { }

            logger.LogInformation("PR #{Number} review 完成", prNumber);
        }
        finally
        {
            await cliSetup.LogoutAsync(ct);
        }
    }
}
```

- [ ] **Step 2: 验证构建**

Run:
```bash
dotnet build demo/GitHubAdaptor
```
Expected: 构建成功

- [ ] **Step 3: 提交**

```bash
git add demo/GitHubAdaptor/Handlers/
git commit -m "🔄 实现 PullRequestHandler：review 流程编排"
```

---

### Task 7: 编写 Program.cs 与配置文件

**Files:**
- Create: `demo/GitHubAdaptor/Program.cs`
- Create: `demo/GitHubAdaptor/appsettings.json`
- Create: `demo/GitHubAdaptor/appsettings.Development.json`

- [ ] **Step 1: 编写 Program.cs**

替换 `demo/GitHubAdaptor/Program.cs`：

```csharp
using GitHubAdaptor.Models;
using GitHubAdaptor.Webhook;
using ManInBlack.AI;
using ManInBlack.AI.DependencyInjection;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddManInBlackSettings();

var githubSettings = new GitHubSettings();
builder.Configuration.GetSection("GitHub").Bind(githubSettings);

builder.Services.AddSerilog(loggerConfig => loggerConfig.ReadFrom.Configuration(builder.Configuration));
builder.Services.AddHttpClient();
builder.Services.AddSingleton(githubSettings);
builder.Services.AddManInBlackFromConfiguration(builder.Configuration);
builder.Services.AddAutoRegisteredServices();

var app = builder.Build();

var factory = app.Services.GetRequiredService<AgentFactory>();
factory.RegisterPipeline("github", pipeline => pipeline.UseDefault());

app.UseMiddleware<GitHubWebhookMiddleware>();

app.MapPost(githubSettings.WebhookEndpoint, async (
    HttpContext context,
    GitHubEventDispatcher dispatcher) =>
{
    var body = (string)context.Items["RawBody"]!;
    var eventType = context.Request.Headers["X-GitHub-Event"].FirstOrDefault() ?? "";

    _ = Task.Run(async () =>
    {
        try
        {
            await dispatcher.DispatchAsync(eventType, body);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "处理 GitHub 事件失败");
        }
    });

    return Results.Ok();
});

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();
```

- [ ] **Step 2: 创建 appsettings.json**

创建 `demo/GitHubAdaptor/appsettings.json`：

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft.AspNetCore": "Warning"
      }
    }
  },
  "AllowedHosts": "*"
}
```

- [ ] **Step 3: 创建 appsettings.Development.json**

创建 `demo/GitHubAdaptor/appsettings.Development.json`：

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetCore": "Information"
    }
  }
}
```

- [ ] **Step 4: 验证构建**

Run:
```bash
dotnet build demo/GitHubAdaptor
```
Expected: 构建成功，无错误

- [ ] **Step 5: 提交**

```bash
git add demo/GitHubAdaptor/Program.cs demo/GitHubAdaptor/appsettings*.json
git commit -m "🚀 编写 Program.cs 与配置文件"
```

---

### Task 8: 创建 Dockerfile

**Files:**
- Create: `demo/GitHubAdaptor/Dockerfile`

- [ ] **Step 1: 创建 Dockerfile**

创建 `demo/GitHubAdaptor/Dockerfile`（基于 dotnet new 生成的标准模板，加装 gh CLI）：

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["demo/GitHubAdaptor/GitHubAdaptor.csproj", "demo/GitHubAdaptor/"]
RUN dotnet restore "demo/GitHubAdaptor/GitHubAdaptor.csproj"
COPY . .
WORKDIR "/src/demo/GitHubAdaptor"
RUN dotnet build "./GitHubAdaptor.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./GitHubAdaptor.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app

# 安装 gh CLI
RUN apt-get update && apt-get install -y gh && rm -rf /var/lib/apt/lists/*

COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "GitHubAdaptor.dll"]
```

- [ ] **Step 2: 提交**

```bash
git add demo/GitHubAdaptor/Dockerfile
git commit -m "🐳 添加 Dockerfile（加装 gh CLI）"
```

---

### Task 9: 更新 settings.json 配置与文档

**Files:**
- Modify: `~/.man-in-black/settings.json`（添加 GitHub 配置和 github-reviewer agent）
- Modify: `docs/quick-start.md` 或相关文档

- [ ] **Step 1: 在 settings.json 中添加 GitHub 配置节**

在 `~/.man-in-black/settings.json` 中添加（如果尚未存在）：

```json
{
  "GitHub": {
    "AppId": 0,
    "PrivateKeyPath": "",
    "WebhookSecret": "",
    "WebhookEndpoint": "/github/webhook"
  },
  "Agents": {
    "github-reviewer": {
      "Description": "GitHub 代码审查员",
      "Instruction": "你是一个专业的代码审查员。审查 PR diff 中的代码变更，识别 bug、安全漏洞、性能问题和可维护性隐患。对不确定的上下文，使用 gh api 读取相关文件全文。使用 gh api 提交 review，包含精确到行的行内评论和 PR 级总结。根据严重程度选择 APPROVE / REQUEST_CHANGES / COMMENT。",
      "PipelineName": "github",
      "SubAgents": [],
      "ModelChoiceName": "default"
    }
  }
}
```

注意：合并到现有的 settings.json 中，不要覆盖已有的 Providers、ModelChoices、其他 Agents。

- [ ] **Step 2: 提交**

```bash
git add -A
git commit -m "📝 添加 GitHub App 配置与 agent 定义"
```

---

## Self-Review

**1. Spec 覆盖检查：**

| 设计要求 | 对应 Task |
|---------|----------|
| GitHub App 注册与权限 | Task 2 (GitHubSettings), Task 3 (token 服务) |
| gh CLI 替代自定义 AiTool | Task 4 (GitHubCliSetup), Task 6 (PullRequestHandler 中 agent 通过 CommandLineTools 调用 gh) |
| HMAC-SHA256 签名验证 | Task 5 (GitHubWebhookMiddleware) |
| 事件路由 (pull_request opened/synchronize) | Task 5 (GitHubEventDispatcher) |
| JWT → installation token 换取 + 缓存 | Task 3 (GitHubAppTokenService) |
| Diff + 按需读取完整文件 | Task 6 (agent system prompt 中指导 gh api) |
| 行内评论 + PR 总结 | Task 6 (agent system prompt 中指导 gh api reviews) |
| APPROVE / REQUEST_CHANGES | Task 6 (agent system prompt) |
| 并发控制 (installation_id + pr_number 隔离) | Task 6 (parentId 参数) |
| 配置加载 (~/.man-in-black/settings.json) | Task 7 (Program.cs AddManInBlackSettings) |
| Docker 部署 (dotnet new 模板 + gh) | Task 8 (Dockerfile) |
| 健康检查 | Task 7 (GET /health) |
| 环境变量覆盖敏感配置 | Task 7 (ASP.NET Core 标准配置优先级) |

**2. 占位符扫描：** 无 TBD / TODO / "implement later" 等。

**3. 类型一致性：** 所有类型定义在各 Task 中一致使用。
