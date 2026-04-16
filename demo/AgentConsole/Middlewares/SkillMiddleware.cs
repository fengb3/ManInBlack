using AgentConsole.Tools;
using ManInBlack.AI.Middleware;
using Microsoft.Extensions.AI;

namespace AgentConsole.Middlewares;

/// <summary>
/// 给agent 添加skill能力
/// 1. system prompt 注入提示词
/// 2. 添加 skill tool
/// </summary>
public class SkillMiddleware(SkillTools skillTools) : AgentMiddleware
{

    public override IAsyncEnumerable<ChatResponseUpdate> HandleAsync(AgentContext context, Func<IAsyncEnumerable<ChatResponseUpdate>> next, CancellationToken cancellationToken = default)
    {
        var systemMessage = context.Messages.FirstOrDefault(m => m.Role == ChatRole.System);

        var oriSystemPrompt = systemMessage?.Text ?? string.Empty;

        var skillDescriptions = skillTools.GetDescriptions();

        oriSystemPrompt +=
            $"""

             ## Available Skills
             {skillDescriptions}

             When a task matches one of the skills above, call the `{nameof(skillTools.LoadSkill)}` tool first to load the full skill instructions, then follow them.
             all the skill's scripts are located under "skills/<skill_name>/scripts/".
             """;

        // 替换或插入新的 system prompt
        if (systemMessage is not null)
            context.Messages.Remove(systemMessage);
        context.Messages.Insert(0, new ChatMessage(ChatRole.System, oriSystemPrompt));

        // 注入 skill tool 声明
        context.Options       ??= new ChatOptions();
        context.Options.Tools ??= [];
        foreach (var tool in SkillTools.AllToolDeclarations)
            context.Options.Tools.Add(tool);

        return next();
    }
}