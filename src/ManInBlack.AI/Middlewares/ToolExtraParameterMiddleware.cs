using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using ManInBlack.AI.Abstraction.Attributes;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Abstraction.Tools;
using ManInBlack.AI.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace ManInBlack.AI.Middlewares;

/// <summary>
/// 运行时为每个工具的 JSON Schema 追加一个额外参数(如 reason/purpose),
/// 让 LLM 调用工具时说明意图,供 UI 或日志展示。
/// <para>
/// 参数从 <see cref="ManInBlackSettings.ToolExtraParameter"/> 读取,
/// 可经 settings.json 的 "ToolExtraParameter" 节或流式扩展
/// <c>AddToolExtraParameter(...)</c> 配置。
/// </para>
/// <para>
/// 必须注册在 <see cref="ToolsMiddleware"/> 之后、<see cref="AgentLoopMiddleware"/> 之前。
/// 典型:<c>UseDefault(b =&gt; b.Use&lt;ToolExtraParameterMiddleware&gt;())</c>。
/// </para>
/// <para>
/// 追加的参数不会出现在工具方法签名上,源生成器 handler 不会提取它,
/// 值会留在 <c>ToolExecuteContext.Arguments</c> 中,由 <c>AgentLifecycleFilter</c>
/// 随 <c>BeforeToolExecuteEvent.ArgumentsJson</c> 一起发布,供 UI 消费。
/// </para>
/// </summary>
[ServiceRegister.Scoped]
public class ToolExtraParameterMiddleware(IOptions<ManInBlackSettings> settings) : AgentMiddleware
{
    private readonly string _paramName = settings.Value.ToolExtraParameter.ParamName;
    private readonly string _paramDescription = settings.Value.ToolExtraParameter.ParamDescription;
    private readonly bool _required = settings.Value.ToolExtraParameter.Required;

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

        // 幂等:同名参数已存在则跳过
        if (properties.ContainsKey(_paramName))
            return original;

        properties[_paramName] = new JsonObject
        {
            ["type"] = "string",
            ["description"] = _paramDescription,
        };

        if (_required)
        {
            if (!schemaNode.ContainsKey("required"))
                schemaNode["required"] = new JsonArray();
            schemaNode["required"]!.AsArray().Add(_paramName);
        }

        return new ToolFunctionDeclaration(
            original.Name,
            original.Description ?? string.Empty,
            schemaNode.ToJsonString(),
            original.ReturnJsonSchema?.GetRawText());
    }
}
