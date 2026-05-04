namespace ManInBlack.AI.Configuration;

/// <summary>
/// 单条钩子配置
/// </summary>
public class HookSettings
{
    /// <summary>钩子名称，用于日志和调试</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>挂载点名称，对应 <see cref="Abstraction.Hooks.HookPoint"/> 枚举</summary>
    public string HookPoint { get; set; } = string.Empty;

    /// <summary>
    /// 脚本路径。全局钩子相对于 {RootPath}/hooks/，用户钩子相对于 {workspace}/。
    /// 推荐将脚本放在 {workspace}/.agents/hooks/ 目录下。
    /// </summary>
    public string Script { get; set; } = string.Empty;

    /// <summary>仅对指定工具名生效（仅 BeforeToolExecute / AfterToolExecute 有效），为空表示所有工具</summary>
    public List<string> ToolNames { get; set; } = [];

    /// <summary>脚本执行超时时间（毫秒），默认 10000</summary>
    public int TimeoutMs { get; set; } = 10000;

    /// <summary>是否启用</summary>
    public bool Enabled { get; set; } = true;
}
