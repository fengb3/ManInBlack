using ManInBlack.AI.Abstraction;
using Microsoft.Extensions.Configuration;

namespace ManInBlack.AI.Configuration;

public static class SettingsLoader
{
    public static ManInBlackSettings Load()
    {
        var configuration = ManInBlackConfigurationBuilder.BuildConfiguration();
        var settings = new ManInBlackSettings();
        configuration.Bind(settings);
        return settings;
    }

    public static ModelChoice ToModelChoice(this ManInBlackSettings settings)
    {
        var provider = CreateProvider(settings.Provider);
        provider.ApiKey = settings.ApiKey;
        if (settings.BaseUrl is not null)
            provider.BaseUrl = settings.BaseUrl;

        return new ModelChoice
        {
            Provider = provider,
            ModelId = settings.ModelId,
        };
    }

    static ModelProvider CreateProvider(string name) => name switch
    {
        "OpenAI" => new OpenAIProvider(),
        "Anthropic" => new AnthropicProvider(),
        "Gemini" => new GeminiProvider(),
        "KimiCN" or "Kimi-cn" => new KimiCNProvider(),
        "KimiAI" or "Kimi-ai" => new KimiAIProvider(),
        "DeepSeek" => new DeepSeekProvider(),
        "Qwen" => new QwenProvider(),
        "Zhipu" => new ZhipuProvider(),
        "ZhipuCodingPlan" => new ZhipuCodingPlanProvider(),
        "Yi" => new YiProvider(),
        "Baichuan" => new BaichuanProvider(),
        "StepFun" => new StepFunProvider(),
        "Spark" => new SparkProvider(),
        "Doubao" => new DoubaoProvider(),
        "MiniMax" => new MiniMaxProvider(),
        _ => throw new NotSupportedException($"不支持的 Provider: {name}"),
    };
}
