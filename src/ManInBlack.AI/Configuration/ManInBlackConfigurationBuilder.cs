using System.Text.Json;
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
    /// 确保配置文件存在，不存在则创建默认配置
    /// </summary>
    internal static void EnsureSettingsFile()
    {
        if (!File.Exists(SettingsPath))
        {
            Directory.CreateDirectory(SettingsRoot);
            var defaults = new ManInBlackSettings();
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(defaults, JsonWriteOptions));
        }
    }
}
