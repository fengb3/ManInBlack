using System.Runtime.CompilerServices;
using ManInBlack.AI.Core;
using ManInBlack.AI.Core.Attributes;
using ManInBlack.AI.Core.Middleware;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ManInBlack.AI.Middlewares;

/// <summary>
/// 从 AgentRoot 读取 Markdown 配置文件，注入系统提示词。
/// 若文件不存在则自动创建模板。
/// </summary>
[ServiceRegister.Scoped]
public class AgentProfileMiddleware(ILogger<AgentProfileMiddleware> logger) : AgentMiddleware
{
    /// <summary>
    /// 默认的 Agent 配置文件名列表
    /// </summary>
    private static readonly string[] ProfileFiles = ["role.md", "work.md", "behavior.md"];

    public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(
        AgentContext context,
        ChatResponseUpdateHandler next,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var workspace = context.ServiceProvider.GetRequiredService<IUserWorkspace>();
        var agentRoot = workspace.AgentRoot;

        Directory.CreateDirectory(agentRoot);

        var promptParts = new List<string>();

        foreach (var fileName in ProfileFiles)
        {
            var filePath = Path.Combine(agentRoot, fileName);

            if (!File.Exists(filePath))
            {
                var template = GetTemplate(fileName);
                await File.WriteAllTextAsync(filePath, template, ct);
                logger.LogInformation("已创建 Agent 配置模板: {FilePath}", filePath);
            }

            var content = await File.ReadAllTextAsync(filePath, ct);
            if (!string.IsNullOrWhiteSpace(content))
            {
                promptParts.Add(content.Trim());
            }
        }

        if (promptParts.Count > 0)
        {
            var profilePrompt = string.Join("\n\n---\n\n", promptParts);
            context.SystemPrompt += "You are a helpful agent. Please follow the instructions below to understand your role, work, and behavior guidelines.";
            context.SystemPrompt += "\n\n" + profilePrompt;
            logger.LogInformation("已注入 Agent 配置提示词，共 {Count} 个文件", promptParts.Count);
        }

        await foreach (var update in next().WithCancellation(ct))
            yield return update;
    }

    /// <summary>
    /// 根据文件名返回对应的模板内容
    /// </summary>
    private static string GetTemplate(string fileName) => fileName switch
    {
        "role.md" => """
            # Agent Role

            <!-- 在此描述 Agent 的角色定位、人设、性格等 -->
            <!-- 例如：你是一个专业的技术助手，擅长 .NET 开发，语气简洁专业 -->
            """,
        "work.md" => """
            # Agent Work

            <!-- 描述 Agent 负责的任务、职责、知识范围 -->
            <!-- 例如：回答编程问题、代码审查、架构建议 -->
            """,
        "behavior.md" => """
            # Agent Behavior

            <!-- 定义 Agent 的交互风格、限制、行为准则 -->
            <!-- 例如：回答准确，不确定时明确说明不猜测 -->
            """,
        _ => $"# {Path.GetFileNameWithoutExtension(fileName)}\n\n<!-- Agent 配置 -->\n"
    };
}