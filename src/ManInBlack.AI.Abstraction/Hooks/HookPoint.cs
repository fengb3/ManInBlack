namespace ManInBlack.AI.Abstraction.Hooks;

/// <summary>
/// 钩子挂载点，对应 Agent 执行生命周期的具体节点
/// </summary>
public enum HookPoint
{
    /// <summary>LLM 调用前（可注入上下文、修改 SystemPrompt）</summary>
    BeforeLlmCall,

    /// <summary>LLM 响应后（可检查响应内容）</summary>
    AfterLlmCall,

    /// <summary>工具执行前（可阻断、检查参数）</summary>
    BeforeToolExecute,

    /// <summary>工具执行后（可检查结果）</summary>
    AfterToolExecute,

    /// <summary>所有工具执行完毕（批量后处理）</summary>
    AllToolsCompleted,

    /// <summary>Agent 循环结束（最终响应前）</summary>
    AgentCompleted,

    /// <summary>斜杠命令执行后(可记录命令名/参数/成功与否)</summary>
    AfterCommand,
}
