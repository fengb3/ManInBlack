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
