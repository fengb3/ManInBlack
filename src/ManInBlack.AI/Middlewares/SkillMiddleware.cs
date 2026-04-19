using System.Runtime.CompilerServices;
using ManInBlack.AI.Core.Middleware;
using ManInBlack.AI.Tools;
using Microsoft.Extensions.AI;

namespace ManInBlack.AI.Middlewares;

/// <summary>
/// 给agent 添加skill能力
/// 1. system prompt 注入提示词
/// 2. 添加 skill tool
/// </summary>
public class SkillMiddleware(SkillService skillService) : AgentMiddleware
{

    public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(AgentContext context,
        ChatResponseUpdateHandler next, [EnumeratorCancellation] CancellationToken ct = default)
    {
        // 有技能时才注入提示词和tool声明
        if (skillService.HasSkills())
        {
            var skillDescriptions = skillService.GetDescriptions();

            context.SystemPrompt +=
                $"""

                 ## Available Skills
                 {skillDescriptions}

                 When a task matches one of the skills above, call the `LoadSkill` tool first to load the full skill instructions, then follow them.
                 all the skill's scripts are located under "skills/<skill_name>/scripts/".
                 """;

            // 注入 skill tool 声明
            context.Options       ??= new ChatOptions();
            context.Options.Tools ??= [];
            foreach (var tool in SkillTools.AllToolDeclarations)
                context.Options.Tools.Add(tool);
        }

        await foreach (var update in next().WithCancellation(ct))
            yield return update;
    }
}