using System.Runtime.CompilerServices;
using System.Text.Json;
using ManInBlack.AI.Abstraction.Attributes;
using ManInBlack.AI.Abstraction.Commands;
using ManInBlack.AI.Abstraction.Hooks;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Commands;
using ManInBlack.AI.Events;
using ManInBlack.AI.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace ManInBlack.AI.Middlewares;

/// <summary>
/// 命令中间件:拦截 <c>/</c>-开头的用户输入,按命令名派发给 <see cref="SlashCommandRegistry"/>。
/// 命令可短路(不调 next)或改完 context 继续 LLM(调 next)。命令执行后发布
/// <see cref="CommandExecutedEvent"/> 并触发 <see cref="HookPoint.AfterCommand"/> 脚本。
/// </summary>
[ServiceRegister.Scoped]
public sealed partial class CommandMiddleware(
    SlashCommandRegistry registry,
    EventBus eventBus,
    IHookExecutor hookExecutor,
    ILogger<CommandMiddleware> logger) : AgentMiddleware
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(
        AgentContext context, ChatResponseUpdateHandler next,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!UserInputCommandHelper.FetchCommand(context.UserInput, out var name, out var args))
        {
            await foreach (var u in next().WithCancellation(ct)) yield return u;
            yield break;
        }

        if (!registry.TryGet(name!, out var handler))
        {
            yield return new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                Contents = [new TextContent($"未知命令 /{name}。输入 /help 查看可用命令。")],
            };
            yield break;
        }

        context.Items[SlashCommandItems.Args] = args!;
        var status = new CommandRunStatus();   // Succeeded = false 直到正常枚举到底
        try
        {
            await foreach (var u in handler!.ExecuteAsync(context, next, ct).WithCancellation(ct))
                yield return u;
            status.Succeeded = true;
        }
        finally
        {
            // 异常路径下 status.Succeeded 仍为 false;异常本身继续向上抛
            await PublishAndHookAsync(context, handler!, args!, status.Succeeded, ct);
        }
    }

    private async Task PublishAndHookAsync(
        AgentContext context, ICommandHandler handler, string[] args, bool succeeded, CancellationToken ct)
    {
        var key = context.AgentId;

        await eventBus.PublishAsync(key, new CommandExecutedEvent
        {
            AgentId = key,
            CommandName = handler.CommandName,
            Args = args,
            Succeeded = succeeded,
        }, ct);

        var hookCtx = new HookContext
        {
            HookPoint = HookPoint.AfterCommand.ToString(),
            AgentId = key,
            CommandName = handler.CommandName,
            CommandArgs = JsonSerializer.Serialize(args, JsonOpts),
            Succeeded = succeeded,
            Properties = BuildProps(context),
        };
        try
        {
            await hookExecutor.ExecuteAsync(HookPoint.AfterCommand, hookCtx, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AfterCommand 脚本执行异常: {Cmd}", handler.CommandName);
        }
    }

    private static Dictionary<string, string> BuildProps(AgentContext context)
    {
        var props = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(context.RootUserId)) props["RootUserId"] = context.RootUserId;
        if (!string.IsNullOrEmpty(context.SessionId))  props["SessionId"]  = context.SessionId;
        if (!string.IsNullOrEmpty(context.ParentId))   props["ParentId"]   = context.ParentId;
        if (!string.IsNullOrEmpty(context.ParentType)) props["ParentType"] = context.ParentType;
        if (!string.IsNullOrEmpty(context.AgentName))  props["AgentName"]  = context.AgentName;
        return props;
    }
}

file sealed class CommandRunStatus { public bool Succeeded; }
