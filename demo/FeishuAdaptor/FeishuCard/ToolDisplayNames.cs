using ManInBlack.AI.Tools;

namespace FeishuAdaptor.FeishuCard;

/// <summary>
/// 工具方法名 → 中文显示名映射，供各类卡片视图复用。
/// 本地工具精确匹配；MCP 工具（{server}__{tool}，server 名可变）按工具名后缀模糊匹配。
/// </summary>
public static class ToolDisplayNames
{
    private static readonly Dictionary<string, string> Map = new()
    {
        // CommandLineTools
        { nameof(CommandLineTools.RunBash), "💻 执行命令" },
        { nameof(CommandLineTools.GetBackgroundTaskResult), "📥 获取后台任务结果" },
        { nameof(CommandLineTools.KillBackgroundTask), "🛑 终止后台任务" },
        // FileTools
        { nameof(FileTools.Read), "📖 读取文件" },
        { nameof(FileTools.Write), "✍️ 写入文件" },
        { nameof(FileTools.Edit), "📝 更新文件" },
        { nameof(FileTools.Glob), "🔎 搜索文件" },
        { nameof(FileTools.Grep), "🔍 搜索内容" },
        // SkillTools
        { nameof(SkillTools.LoadSkill), "🧠 加载技能" },
    };

    /// <summary>
    /// 根据工具方法名获取中文显示名。本地工具精确匹配；未命中时按 MCP 后缀模糊匹配。
    /// </summary>
    public static string Get(string? toolName)
    {
        if (toolName is not null && Map.TryGetValue(toolName, out var displayName))
            return displayName;
        return FuzzyMcp(toolName);
    }

    /// <summary>
    /// MCP 工具显示名模糊匹配：server 名可变，按工具名后缀（search/reader/fetch）归类。
    /// </summary>
    private static string FuzzyMcp(string? toolName)
    {
        if (string.IsNullOrEmpty(toolName)) return "未知工具";
        var lower = toolName.ToLowerInvariant();
        if (lower.Contains("search") || lower.Contains("web")) return "🌐 联网搜索";
        if (lower.Contains("reader") || lower.Contains("fetch")) return "📄 网页读取";
        return toolName;
    }
}
