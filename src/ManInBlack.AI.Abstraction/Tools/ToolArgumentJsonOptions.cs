using System.Text.Json;
using System.Text.Json.Serialization;

namespace ManInBlack.AI.Abstraction.Tools;

/// <summary>
/// 源生成器生成的 [AiTool] handler 反序列化 <c>JsonElement</c> 参数时使用的共享选项。
/// LLM 可能回传 camelCase 或 PascalCase，统一大小写不敏感匹配。
/// </summary>
public static class ToolArgumentJsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
