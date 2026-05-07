using System.Runtime.CompilerServices;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Services;
using ManInBlack.AI.ToolCallFilters;
using Microsoft.Extensions.AI;

namespace AgentConsole.Middlewares;

/// <summary>
/// 控制台输出中间件，将 Agent 响应流式输出到控制台，
/// 包括推理过程、文本内容和工具调用事件的显示
/// </summary>
public class ConsoleOutputMiddleware(EventBus eventBus) : AgentMiddleware
{
    private string _last = "";

    public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(
        AgentContext context,
        ChatResponseUpdateHandler next,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // 订阅工具调用事件
        eventBus.Subscribe<ToolExecutingEvent>(async (@event, ct) =>
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n[Tool Call] {@event.ToolName}({string.Join(", ", @event.Arguments.Select(kv => $"{kv.Key}: {kv.Value}"))})");
            Console.ResetColor();
        });

        eventBus.Subscribe<ToolExecutedEvent>(async (@event, ct) =>
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[Tool Result] {@event.Result} {@event.Exception}");
            Console.ResetColor();
        });

        // 流式输出 Agent 响应
        await foreach (var update in next().WithCancellation(ct))
        {
            foreach (var content in update.Contents)
            {
                switch (content)
                {
                    case TextReasoningContent reasoning:
                        if (_last != "reasoning")
                            Console.WriteLine("[Reasoning]");
                        _last = "reasoning";
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.Write(reasoning.Text);
                        Console.ResetColor();
                        break;
                    case TextContent text:
                        if (_last != "text")
                            Console.WriteLine();
                        _last = "text";
                        Console.Write(text.Text);
                        break;
                    case UsageContent:
                        break;
                }
            }
            yield return update;
        }

        // 输出 Token 用量
        Console.WriteLine();
        Console.WriteLine();
        var usage = context.AccumulatedUsage;
        if (usage.InputTokenCount is not null || usage.OutputTokenCount is not null)
        {
            Console.WriteLine($"Token 用量 — 输入: {usage.InputTokenCount}, 输出: {usage.OutputTokenCount}, 总计: {usage.TotalTokenCount}, 缓存: {usage.CachedInputTokenCount}");
        }
    }
}
