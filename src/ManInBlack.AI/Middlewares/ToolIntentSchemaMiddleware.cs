using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Abstraction.Tools;
using Microsoft.Extensions.AI;

namespace ManInBlack.AI.Middlewares;

/// <summary>
/// 运行时为每个工具的 JSON Schema 追加额外参数（如 reason/intent），
/// 让 LLM 调用工具时说明意图，供 UI 或日志展示。
/// <para>
/// 必须注册在 <see cref="ToolsMiddleware"/> 之后、<see cref="AgentLoopMiddleware"/> 之前。
/// 推荐通过 <c>UseDefault(b =&gt; b.Use(new ToolIntentSchemaMiddleware(...)))</c> 插入。
/// </para>
/// <para>
/// 追加的参数不会出现在工具方法签名上，源生成器 handler 不会提取它，
/// 值会留在 <c>ToolExecuteContext.Arguments</c> 中，由 <c>AgentLifecycleFilter</c>
/// 随 <c>BeforeToolExecuteEvent.ArgumentsJson</c> 一起发布，供 UI 消费。
/// </para>
/// </summary>
public class ToolIntentSchemaMiddleware(
    string paramName = "reason",
    string paramDescription = "Briefly explain what you intend to accomplish by calling this tool.",
    bool required = false) : AgentMiddleware
{
    public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(
        AgentContext context,
        ChatResponseUpdateHandler next,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (context.Options?.Tools is { Count: > 0 } tools)
        {
            for (var i = 0; i < tools.Count; i++)
            {
                if (tools[i] is AIFunctionDeclaration decl)
                    tools[i] = DecorateSchema(decl);
            }
        }

        await foreach (var update in next().WithCancellation(ct))
            yield return update;
    }

    private AIFunctionDeclaration DecorateSchema(AIFunctionDeclaration original)
    {
        var schemaNode = JsonNode.Parse(original.JsonSchema.GetRawText())?.AsObject()
            ?? throw new InvalidOperationException(
                $"工具 '{original.Name}' 的 JsonSchema 不是有效的 JSON 对象。");

        // 确保 "properties" 节点存在
        if (!schemaNode.ContainsKey("properties"))
            schemaNode["properties"] = new JsonObject();

        var properties = schemaNode["properties"]!.AsObject();

        // 幂等：同名参数已存在则跳过
        if (properties.ContainsKey(paramName))
            return original;

        properties[paramName] = new JsonObject
        {
            ["type"] = "string",
            ["description"] = paramDescription,
        };

        if (required)
        {
            if (!schemaNode.ContainsKey("required"))
                schemaNode["required"] = new JsonArray();
            schemaNode["required"]!.AsArray().Add(paramName);
        }

        return new ToolFunctionDeclaration(
            original.Name,
            original.Description ?? string.Empty,
            schemaNode.ToJsonString(),
            original.ReturnJsonSchema?.GetRawText());
    }
}
