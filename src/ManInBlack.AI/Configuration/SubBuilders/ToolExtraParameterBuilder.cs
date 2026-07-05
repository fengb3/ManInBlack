namespace ManInBlack.AI.Configuration;

/// <summary>
/// 流式构建 <see cref="ToolExtraParameterSettings"/>。
/// </summary>
public sealed class ToolExtraParameterBuilder
{
    internal ToolExtraParameterSettings Settings { get; } = new();

    /// <summary>设置追加参数名(默认 "reason")。</summary>
    public ToolExtraParameterBuilder ParamName(string paramName)
    { Settings.ParamName = paramName; return this; }

    /// <summary>设置追加参数的描述(LLM 可见)。</summary>
    public ToolExtraParameterBuilder ParamDescription(string description)
    { Settings.ParamDescription = description; return this; }

    /// <summary>设置是否在 schema 的 required 中标记此参数。</summary>
    public ToolExtraParameterBuilder Required(bool required)
    { Settings.Required = required; return this; }
}
