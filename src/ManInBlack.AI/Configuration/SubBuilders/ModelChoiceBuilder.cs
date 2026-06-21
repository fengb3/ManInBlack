namespace ManInBlack.AI.Configuration;

/// <summary>
/// 流式构建 ModelChoiceSettings。
/// </summary>
public sealed class ModelChoiceBuilder
{
    internal ModelChoiceSettings Settings { get; } = new();

    public ModelChoiceBuilder Provider(string providerName) { Settings.ProviderName = providerName; return this; }
    public ModelChoiceBuilder ModelId(string modelId) { Settings.ModelId = modelId; return this; }
}
