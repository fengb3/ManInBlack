using System.Text.Json;
using ManInBlack.AI.Abstraction.Storage;
using Microsoft.Extensions.Configuration;

namespace ManInBlack.AI.Configuration;

public static class ManInBlackConfigurationBuilder
{
    static readonly string SettingsRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".man-in-black");

    static readonly string SettingsPath = Path.Combine(SettingsRoot, "settings.json");

    static readonly JsonSerializerOptions JsonWriteOptions = new() { WriteIndented = true };

    /// <summary>
    /// 构建 IConfiguration，读取 ~/.man-in-black/settings.json 并启用 reloadOnChange
    /// </summary>
    public static IConfiguration BuildConfiguration()
    {
        EnsureSettingsFile();
        return new ConfigurationBuilder()
            .AddJsonFile(SettingsPath, optional: false, reloadOnChange: true)
            .Build();
    }

    /// <summary>
    /// 将 ManInBlack 配置源添加到已有 IConfigurationBuilder（用于 WebApplicationBuilder 等场景）
    /// </summary>
    public static IConfigurationBuilder AddManInBlackSettings(this IConfigurationBuilder builder)
    {
        EnsureSettingsFile();
        return builder.AddJsonFile(SettingsPath, optional: false, reloadOnChange: true);
    }

    /// <summary>
    /// 读取 ~/.man-in-black/settings.json（缺失则创建默认）并绑定到 ManInBlackSettings。
    /// 供 .UseJson() 复用。
    /// </summary>
    public static ManInBlackSettings LoadSettings()
    {
        EnsureSettingsFile();
        return LoadSettingsFromFile(SettingsPath);
    }

    /// <summary>
    /// 从指定路径读取 JSON 配置并绑定到 ManInBlackSettings（不确保文件存在，调用方负责）。
    /// </summary>
    internal static ManInBlackSettings LoadSettingsFromFile(string path)
    {
        var settings = new ManInBlackSettings();
        new ConfigurationBuilder().AddJsonFile(path, optional: false).Build().Bind(settings);
        return settings;
    }

    /// <summary>
    /// 确保配置文件存在，不存在则创建默认配置
    /// </summary>
    internal static void EnsureSettingsFile()
    {
        if (!File.Exists(SettingsPath))
        {
            Directory.CreateDirectory(SettingsRoot);
            var defaults = new ManInBlackSettings
            {
                Providers = new Dictionary<string, ProviderSettings>
                {
                    ["default"] = new ProviderSettings
                    {
                        Schema = "OpenAI",
                        ApiKey = "",
                    }
                },
                ModelChoices = new Dictionary<string, ModelChoiceSettings>
                {
                    ["default"] = new ModelChoiceSettings
                    {
                        ProviderName = "default",
                        ModelId = "gpt-4o",
                    }
                },
                Storage = new StorageSettings(),
            };
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(defaults, JsonWriteOptions));
        }
    }
}
