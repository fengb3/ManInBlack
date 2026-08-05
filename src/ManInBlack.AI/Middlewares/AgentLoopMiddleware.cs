using System.Runtime.CompilerServices;
using System.Text;
using ManInBlack.AI.Abstraction.Attributes;
using ManInBlack.AI.Abstraction.Hooks;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Abstraction.Tools;
using ManInBlack.AI.Events;
using ManInBlack.AI.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ManInBlack.AI.Middlewares;

/// <summary>
/// Agent 循环中间件，自动处理模型返回的 tool call 并将结果追加到消息历史。
/// 通过 EventBus 发布 AfterLlmCallEvent 和 AllToolsCompletedEvent。
/// 工具调用以并行方式执行，最大并发度可通过 <see cref="MaxToolConcurrency"/> 控制。
/// </summary>
[ServiceRegister.Scoped]
public class AgentLoopMiddleware(IToolExecutor toolExecutor, ILogger<AgentContext> logger) : AgentMiddleware
{
    /// <summary>
    /// 单批次内工具调用的最大并发数
    /// </summary>
    private const int MaxToolConcurrency = 5;

    /// <summary>
    /// 工具执行被打断时回填给 LLM 的结果文本，使模型知道该调用未获得结果。
    /// </summary>
    private const string ToolInterruptedMessage = "工具执行已被中断，未获得结果。";

    public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(AgentContext context,
        ChatResponseUpdateHandler next,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var bus = context.ServiceProvider.GetRequiredService<EventBus>();
        var key = context.AgentId;

        while (true)
        {
            var functionCalls    = new List<FunctionCallContent>();
            // 本轮工具结果（本地执行结果），用于回填消息历史
            var toolResults      = new List<AIContent>();
            var textBuilder      = new StringBuilder();
            var reasoningBuilder = new StringBuilder();

            await foreach (var update in next().WithCancellation(ct))
            {
                foreach (var content in update.Contents)
                {
                    switch (content)
                    {
                        case FunctionCallContent fcc:
                            functionCalls.Add(fcc);
                            break;
                        case TextContent text:
                            textBuilder.Append(text.Text);
                            break;
                        case TextReasoningContent reasoning:
                            reasoningBuilder.Append(reasoning.Text);
                            break;
                        case UsageContent usageContent:
                            context.AccumulatedUsage.Add(usageContent.Details);
                            break;
                    }
                }

                yield return update;
            }

            // 构建 assistant 消息内容（text + reasoning + function calls）
            var assistantContents = new List<AIContent>();
            if (reasoningBuilder.Length > 0)
                assistantContents.Add(new TextReasoningContent(reasoningBuilder.ToString()));
            if (textBuilder.Length > 0)
                assistantContents.Add(new TextContent(textBuilder.ToString()));
            assistantContents.AddRange(functionCalls);

            if (assistantContents.Count > 0)
                context.Messages.Add(new ChatMessage(ChatRole.Assistant, assistantContents));

            // ── AfterLlmCall：LLM 响应流结束后触发 ──
            await bus.PublishAsync(EventBus.HookKey(key), new AfterLlmCallEvent
            {
                AgentId = key,
                SystemPrompt = context.SystemPrompt,
                UserInput = context.UserInput,
            }, ct);

            if (functionCalls.Count == 0)
                yield break;

            // ── 工具：经 ToolExecutor 执行（handler 内的 AgentLifecycleFilter 自动发 Before/After 事件）──
            // 预填「中断」桩：保证每个 tool_call_id 都有对应结果。即便工具执行被取消，
            // 消息历史也保持「assistant(tool_calls) → tool(results)」一致，避免下一轮 API 报 400。
            var localResults = new FunctionResultContent[functionCalls.Count];
            for (var i = 0; i < functionCalls.Count; i++)
                localResults[i] = new FunctionResultContent(functionCalls[i].CallId, ToolInterruptedMessage);

            using var semaphore = new SemaphoreSlim(MaxToolConcurrency);
            var tasks = functionCalls.Select(async (fc, i) =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    var toolCtx = new ToolExecuteContext(context.ServiceProvider)
                    {
                        ToolName  = fc.Name,
                        CallId    = fc.CallId,
                        Arguments = fc.Arguments
                    };

                    await toolExecutor.ExecuteAsync(toolCtx, ct);

                    if (toolCtx.Error != null)
                    {
                        logger.LogInformation(toolCtx.Error, "Error executing tool {ToolName} in agent {AgentId}", toolCtx.ToolName, context.AgentId);
                    }

                    localResults[i] = new FunctionResultContent(fc.CallId, toolCtx.Error?.Message ?? toolCtx.Result);
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToArray();

            // 工具执行被取消时，Task.WhenAll 会抛 OperationCanceledException：吞掉并补齐结果，
            // 让历史保持一致后干净退出本轮。
            var interrupted = false;
            try
            {
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                interrupted = true;
            }
            catch (AggregateException ae) when (ct.IsCancellationRequested && ae.Flatten().InnerExceptions.All(e => e is OperationCanceledException))
            {
                // 安全网：await Task.WhenAll 通常抛首个解包异常，多任务同时失败时理论上可能聚合为 OCE。
                interrupted = true;
            }

            // 无条件追加 tool 结果消息（打断路径也补齐，保证历史一致）
            foreach (var result in localResults)
                toolResults.Add(result);
            context.Messages.Add(new ChatMessage(ChatRole.Tool, toolResults));

            // 被打断：历史已一致，直接结束本轮。不再 yield 工具更新（已取消，无人监听）、
            // 不发 AllToolsCompleted、不保存检查点、不回环调 LLM。
            if (interrupted)
                yield break;

            foreach (var result in localResults)
                yield return new ChatResponseUpdate(ChatRole.Tool, [result]);

            // ── AllToolsCompleted：本批次所有工具执行完毕后触发 ──
            await bus.PublishAsync(EventBus.HookKey(key), new AllToolsCompletedEvent
            {
                AgentId = key,
            }, ct);

            // ── 检查点保存 ──
            if (context.Items.TryGetValue("SaveCheckpoint", out var obj) && obj is Func<string?, CancellationToken, Task> save)
            {
                await save("AfterToolCall", ct);
            }
        }
    }
}
