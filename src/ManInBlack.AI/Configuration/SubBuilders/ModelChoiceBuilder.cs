namespace ManInBlack.AI.Configuration;

/// <summary>
/// 流式构建 ModelChoiceSettings。
/// </summary>
public sealed class ModelChoiceBuilder
{
    internal ModelChoiceSettings Settings { get; } = new();

    /// <summary>设置关联的 Provider 名称。</summary>
    public ModelChoiceBuilder Provider(string providerName) { Settings.ProviderName = providerName; return this; }

    /// <summary>设置模型 ID。</summary>
    public ModelChoiceBuilder ModelId(string modelId) { Settings.ModelId = modelId; return this; }
}
