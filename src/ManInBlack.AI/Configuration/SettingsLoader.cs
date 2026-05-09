namespace ManInBlack.AI.Configuration;

public static class SettingsLoader
{
    /// <summary>
    /// 获取默认的 ModelChoice（由 Providers["default"] + ModelChoices["default"] 组合）
    /// </summary>
    public static ModelChoice GetDefaultModelChoice(this ManInBlackSettings settings)
    {
        return settings.GetModelChoice("default");
    }

    /// <summary>
    /// 按 name 获取 ModelChoice
    /// </summary>
    public static ModelChoice GetModelChoice(this ManInBlackSettings settings, string name)
    {
        if (!settings.ModelChoices.TryGetValue(name, out var choice))
            throw new KeyNotFoundException($"未找到 ModelChoice 配置：{name}");

        if (!settings.Providers.TryGetValue(choice.ProviderName, out var provider))
            throw new KeyNotFoundException($"ModelChoice \"{name}\" 引用的 Provider \"{choice.ProviderName}\" 不存在");

        return new ModelChoice
        {
            Schema = provider.Schema,
            ApiKey = provider.ApiKey,
            BaseUrl = provider.BaseUrl ?? "",
            ModelId = choice.ModelId,
        };
    }
}
