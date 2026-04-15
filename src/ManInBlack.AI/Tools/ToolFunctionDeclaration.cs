using System.Text.Json;
using Microsoft.Extensions.AI;

namespace ManInBlack.AI.Tools;

/// <summary>
/// 通过名称、描述和 JSON Schema 字符串构造的 <see cref="AIFunctionDeclaration"/> 具体实现。
/// 由源生成器生成代码时使用。
/// </summary>
public class ToolFunctionDeclaration : AIFunctionDeclaration
{
    private readonly JsonElement _jsonSchema;
    private readonly JsonElement? _returnJsonSchema;

    public ToolFunctionDeclaration(string name, string description, string jsonSchema, string? returnJsonSchema = null)
    {
        Name = name;
        Description = description;
        using var doc = JsonDocument.Parse(jsonSchema);
        _jsonSchema = doc.RootElement.Clone();
        _returnJsonSchema = returnJsonSchema is not null
            ? ParseAndClone(returnJsonSchema)
            : null;
    }

    public override string Name { get; }
    public override string Description { get; }
    public override JsonElement JsonSchema => _jsonSchema;
    public override JsonElement? ReturnJsonSchema => _returnJsonSchema;

    private static JsonElement ParseAndClone(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
