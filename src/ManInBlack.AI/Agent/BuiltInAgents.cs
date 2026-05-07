using ManInBlack.AI.Abstraction.Agent;

namespace ManInBlack.AI.Agent;

/// <summary>
/// 预制 Agent 定义工厂，提供常用的内置 Agent 配置
/// </summary>
public static class BuiltInAgents
{
    /// <summary>
    /// 通用 Agent，拥有所有工具，支持文件操作、命令行执行、技能调用和子 Agent 委派
    /// </summary>
    public static AgentDefinition General(AgentModelOptions? model = null) => new()
    {
        Name = "general",
        Description = "通用 AI 助手，拥有文件读写、命令行执行、技能调用和子 Agent 委派能力",
        Instructions = "你是一个通用 AI 助手，拥有文件读写、命令行执行、技能调用和子 Agent 委派能力。根据任务需要选择合适的工具完成工作。",
        PipelineName = "Default",
        Model = model,
    };

    /// <summary>
    /// 代码专家 Agent，擅长编写、修改和调试代码
    /// </summary>
    public static AgentDefinition Coder(AgentModelOptions? model = null) => new()
    {
        Name = "coder",
        Description = "编写、修改、调试代码的专家",
        Instructions = "你是一个代码专家，专注于编写高质量、可维护的代码。收到任务后直接执行，不要过度解释。",
        PipelineName = "Coder",
        Model = model,
    };

    /// <summary>
    /// Shell 专家 Agent，擅长执行命令行操作和自动化任务
    /// </summary>
    public static AgentDefinition Shell(AgentModelOptions? model = null) => new()
    {
        Name = "shell",
        Description = "执行 shell 命令的专家。擅长系统操作和自动化任务。",
        Instructions = "你是一个命令行专家。直接执行命令并返回结果。",
        PipelineName = "Shell",
        Model = model,
    };

    /// <summary>
    /// 分析专家 Agent，擅长文件分析、代码阅读和模式搜索
    /// </summary>
    public static AgentDefinition Analyst(AgentModelOptions? model = null) => new()
    {
        Name = "analyst",
        Description = "文件分析和搜索专家。擅长阅读代码、查找模式和生成报告。",
        Instructions = "你是一个分析专家，专注于阅读和理解文件内容。仔细分析后给出结构化的分析结果。",
        PipelineName = "Analyst",
        Model = model,
    };
}
