using System.Text.Json;

using ManInBlack.AI.Abstraction;

namespace ManInBlack.AI.Configuration;

public static class SettingsLoader
{
    static readonly string SettingsRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".man-in-black");

    static readonly string SettingsPath = Path.Combine(SettingsRoot, "settings.json");

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static ManInBlackSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            Directory.CreateDirectory(SettingsRoot);
            var defaults = new ManInBlackSettings();
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(defaults, JsonOptions));
        }

        var json = File.ReadAllText(SettingsPath);
        return JsonSerializer.Deserialize<ManInBlackSettings>(json, JsonOptions)
               ?? throw new InvalidOperationException($"配置文件反序列化失败: {SettingsPath}");
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
