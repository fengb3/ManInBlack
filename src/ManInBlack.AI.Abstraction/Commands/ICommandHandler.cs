using ManInBlack.AI.Abstraction.Middleware;
using Microsoft.Extensions.AI;

namespace ManInBlack.AI.Abstraction.Commands;

/// <summary>单个斜杠命令的执行器(由源生成器为每个 [SlashCommand] 方法生成实现)。</summary>
public interface ICommandHandler
{
    string CommandName { get; }
    string[] Aliases { get; }
    string Description { get; }

    IAsyncEnumerable<ChatResponseUpdate> ExecuteAsync(
        AgentContext context, ChatResponseUpdateHandler next, CancellationToken ct);
}

/// <summary>去重后的命令元数据,供 /help 展示。</summary>
public sealed record CommandInfo(string Name, IReadOnlyList<string> Aliases, string Description);
