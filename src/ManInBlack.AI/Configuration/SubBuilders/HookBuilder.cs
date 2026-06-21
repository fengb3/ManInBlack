namespace ManInBlack.AI.Configuration;

/// <summary>
/// 流式构建 HookSettings。
/// </summary>
public sealed class HookBuilder
{
    internal HookSettings Settings { get; } = new();

    /// <summary>设置钩子名称，用于日志和调试。</summary>
    public HookBuilder Name(string name) { Settings.Name = name; return this; }

    /// <summary>设置挂载点名称，对应 HookPoint 枚举。</summary>
    public HookBuilder HookPoint(string hookPoint) { Settings.HookPoint = hookPoint; return this; }

    /// <summary>设置要执行的 Shell 命令。</summary>
    public HookBuilder Run(string script) { Settings.Script = script; return this; }

    /// <summary>添加仅生效的工具名（仅 BeforeToolExecute / AfterToolExecute 有效）。</summary>
    public HookBuilder ToolName(string toolName) { Settings.ToolNames.Add(toolName); return this; }

    /// <summary>设置脚本执行超时时间（毫秒）。</summary>
    public HookBuilder TimeoutMs(int timeoutMs) { Settings.TimeoutMs = timeoutMs; return this; }

    /// <summary>设置是否启用。</summary>
    public HookBuilder Enabled(bool enabled) { Settings.Enabled = enabled; return this; }
}
