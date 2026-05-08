using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using ManInBlack.AI.Abstraction.Attributes;
using ManInBlack.AI.Abstraction.Middleware;
using ManInBlack.AI.Abstraction.Tools;
using ManInBlack.AI.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ManInBlack.AI.Middlewares;

/// <summary>
/// 工具描述覆盖中间件，根据配置和 per-request 覆盖动态修改工具的描述、参数描述和返回值描述。
/// 支持两层覆盖：配置层（IOptionsMonitor）和请求层（AgentContext.ToolDescriptionOverrides），请求层优先。
/// </summary>
[ServiceRegister.Scoped]
public class ToolPromptMiddleware(IOptionsMonitor<ManInBlackSettings>? optionsMonitor,
    ILogger<ToolPromptMiddleware> logger) : AgentMiddleware
{
    private const string AppliedKey = "ToolPromptMiddleware.Applied";

    public override async IAsyncEnumerable<ChatResponseUpdate> HandleAsync(
        AgentContext context,
        ChatResponseUpdateHandler next,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // 幂等性检查：已应用过则直接透传
        if (context.Items.ContainsKey(AppliedKey))
        {
            await foreach (var update in next().WithCancellation(ct))
                yield return update;
            yield break;
        }
        context.Items[AppliedKey] = true;

        // 收集所有 override（config 层 + per-request 层）
        var overrides = BuildOverrides(context);

        // 如果没有任何覆盖，直接透传
        if (overrides.Count == 0)
        {
            await foreach (var update in next().WithCancellation(ct))
                yield return update;
            yield break;
        }

        // 应用覆盖到工具列表
        ApplyOverrides(context.Options?.Tools, overrides);

        await foreach (var update in next().WithCancellation(ct))
            yield return update;
    }

    /// <summary>
    /// 收集配置层和请求层的覆盖，合并为字典（请求层优先）
    /// </summary>
    private Dictionary<string, ToolDescriptionOverride> BuildOverrides(AgentContext context)
    {
        var overrides = new Dictionary<string, ToolDescriptionOverride>();

        // 配置层覆盖（基础层）
        if (optionsMonitor is not null)
        {
            foreach (var setting in optionsMonitor.CurrentValue.ToolDescriptions ?? [])
            {
                overrides[setting.ToolName] = MapSettingToOverride(setting);
            }
        }

        // 请求层覆盖（覆盖配置层）
        if (context.ToolDescriptionOverrides is not null)
        {
            foreach (var ov in context.ToolDescriptionOverrides)
            {
                overrides[ov.ToolName] = ov;
            }
        }

        return overrides;
    }

    /// <summary>
    /// 将覆盖应用到工具声明列表，对匹配的声明创建新实例替换。
    /// ChatOptions.Tools 是 IList{AITool}，其中只有 AIFunctionDeclaration 可被覆盖。
    /// </summary>
    private void ApplyOverrides(IList<AITool>? tools,
        Dictionary<string, ToolDescriptionOverride> overrides)
    {
        if (tools is null || tools.Count == 0) return;

        for (var i = 0; i < tools.Count; i++)
        {
            if (tools[i] is not AIFunctionDeclaration decl) continue;
            if (!overrides.TryGetValue(decl.Name, out var ov)) continue;

            logger.LogDebug("正在覆盖工具 {ToolName} 的描述", decl.Name);
            tools[i] = CreateOverriddenDeclaration(decl, ov);
        }
    }

    /// <summary>
    /// 根据覆盖配置创建新的 ToolFunctionDeclaration 实例
    /// </summary>
    private ToolFunctionDeclaration CreateOverriddenDeclaration(
        AIFunctionDeclaration original, ToolDescriptionOverride ov)
    {
        var newDescription = ov.Description ?? original.Description;
        var newSchema = ModifySchema(original.JsonSchema, ov);
        var newReturnSchema = ov.ReturnsDescription is not null
            ? BuildReturnSchema(ov.ReturnsDescription, original.ReturnJsonSchema)
            : original.ReturnJsonSchema?.GetRawText();

        return new ToolFunctionDeclaration(
            original.Name, newDescription, newSchema, newReturnSchema);
    }

    /// <summary>
    /// 修改 JSON Schema：覆盖参数描述、增加新参数
    /// </summary>
    private static string ModifySchema(JsonElement originalSchema, ToolDescriptionOverride ov)
    {
        var node = JsonNode.Parse(originalSchema.GetRawText())!;
        var root = node.AsObject();

        // 覆盖已有参数的描述
        if (ov.ParameterOverrides is not null && root.TryGetPropertyValue("properties", out var propsNode))
        {
            var props = propsNode!.AsObject();
            foreach (var (paramName, newDesc) in ov.ParameterOverrides)
            {
                if (props.TryGetPropertyValue(paramName, out var paramNode))
                {
                    paramNode!.AsObject()["description"] = newDesc;
                }
            }
        }

        // 添加新增参数
        if (ov.AdditionalParameters is not null)
        {
            if (!root.TryGetPropertyValue("properties", out var existingProps))
            {
                existingProps = new JsonObject();
                root["properties"] = existingProps;
            }

            var properties = existingProps!.AsObject();

            // 确保 required 数组存在
            if (!root.TryGetPropertyValue("required", out var requiredNode))
            {
                requiredNode = new JsonArray();
                root["required"] = requiredNode;
            }
            var required = requiredNode!.AsArray();

            foreach (var param in ov.AdditionalParameters)
            {
                var paramObj = new JsonObject
                {
                    ["type"] = param.Type,
                };

                if (param.IsNullable)
                    paramObj["nullable"] = true;

                if (param.Description is not null)
                    paramObj["description"] = param.Description;

                properties[param.Name] = paramObj;

                if (param.Required)
                    required.Add(param.Name);
            }
        }

        return node.ToJsonString();
    }

    /// <summary>
    /// 构建返回值 JSON Schema
    /// </summary>
    private static string BuildReturnSchema(string returnsDescription, JsonElement? originalReturnSchema)
    {
        if (originalReturnSchema is not null)
        {
            // 保留原始结构，只替换 description
            var node = JsonNode.Parse(originalReturnSchema.Value.GetRawText())!;
            node.AsObject()["description"] = returnsDescription;
            return node.ToJsonString();
        }

        // 没有原始返回值 schema，创建一个简单的描述 schema
        return new JsonObject { ["description"] = returnsDescription }.ToJsonString();
    }

    /// <summary>
    /// 将配置层的 ToolDescriptionSetting 转换为 ToolDescriptionOverride
    /// </summary>
    private static ToolDescriptionOverride MapSettingToOverride(ToolDescriptionSetting setting)
    {
        return new ToolDescriptionOverride
        {
            ToolName = setting.ToolName,
            Description = setting.Description,
            ParameterOverrides = setting.ParameterOverrides,
            ReturnsDescription = setting.ReturnsDescription,
            AdditionalParameters = setting.AdditionalParameters?.ConvertAll(p => new ToolParameterOverride
            {
                Name = p.Name,
                Type = p.Type,
                Description = p.Description,
                Required = p.Required,
                IsNullable = p.IsNullable,
            }),
        };
    }
}
