using GitHubAdaptor.Models;
using GitHubAdaptor.Services;
using ManInBlack.AI;
using ManInBlack.AI.Abstraction.Attributes;
using Microsoft.Extensions.Logging;

namespace GitHubAdaptor.Handlers;

[ServiceRegister.Singleton]
public class PullRequestHandler(
    GitHubAppTokenService tokenService,
    GitHubCliSetup cliSetup,
    AgentFactory agentFactory,
    ILogger<PullRequestHandler> logger)
{
    /// <summary>
    /// 确保同一时间只处理一个 PR，避免 GH_TOKEN 环境变量被并发覆盖
    /// </summary>
    private readonly SemaphoreSlim _semaphore = new(1, 1);

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

        // 串行化 PR 处理，避免 GH_TOKEN 环境变量并发覆盖
        await _semaphore.WaitAsync(ct);
        try
        {
            // 通过环境变量传递 token，子进程（gh CLI、agent shell）会继承
            Environment.SetEnvironmentVariable("GH_TOKEN", token);
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
                            2. 对不确定的上下文，用 `gh api repos/{repo}/contents/<文件路径>` 读取完整文件
                            3. 用 `gh api repos/{repo}/pulls/{prNumber}/reviews` 提交 review，body 为 JSON:
                               - body: 总结内容
                               - event: APPROVE / REQUEST_CHANGES / COMMENT
                               - comments[]: 每个 comment 包含 path（文件路径）、position（diff 中的行号，从 1 开始）、body（评论内容）
                            4. position 是 diff 中的行号（从 1 开始），不是文件行号
                            </github-context>
                            """;
                    },
                    ct);

                await foreach (var _ in updates) { }

                logger.LogInformation("PR #{Number} review 完成", prNumber);
            }
            finally
            {
                Environment.SetEnvironmentVariable("GH_TOKEN", null);
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
