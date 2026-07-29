using System.Runtime.CompilerServices;
using ManInBlack.AI.Abstraction;
using ManInBlack.AI.Abstraction.Attributes;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Abstraction.Storage;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace ManInBlack.AI.Commands;

/// <summary>内置斜杠命令。</summary>
[ServiceRegister.Scoped]
public sealed partial class BuiltinCommands
{
    /// <summary>重置当前会话:换新 SessionId、清空历史,并返回确认。</summary>
    [SlashCommand("new", "重置对话", Aliases = ["clear", "reset"])]
    public async IAsyncEnumerable<ChatResponseUpdate> New(
        AgentContext context, ChatResponseUpdateHandler next,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var userStorage = context.ServiceProvider.GetRequiredService<IUserStorage>();
        context.SessionId = await userStorage.CreateNewSessionIdAsync(context.ParentId);
        context.Messages.Clear();

        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [new TextContent("已重置对话")],
        };
        // 不调 next() → 短路
    }

    /// <summary>列出全部已注册命令及描述。</summary>
    [SlashCommand("help", "显示可用命令")]
    public async IAsyncEnumerable<ChatResponseUpdate> Help(
        AgentContext context, ChatResponseUpdateHandler next,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var registry = context.ServiceProvider.GetRequiredService<SlashCommandRegistry>();

        var lines = registry.Commands
            .Select(c => c.Aliases.Count > 0
                ? $"  /{c.Name} (或 /{string.Join(", /", c.Aliases)}) — {c.Description}"
                : $"  /{c.Name} — {c.Description}")
            .ToList();

        var text = lines.Count == 0
            ? "暂无可用命令。"
            : "可用命令:\n" + string.Join("\n", lines);

        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [new TextContent(text)],
        };
    }
}
