namespace ManInBlack.AI.Core;

/// <summary>
/// 用户工作空间接口，提供用户级文件系统的访问能力
/// </summary>
public interface IUserWorkspace
{
    /// <summary>
    /// 用户标识
    /// </summary>
    string UserId { get; }

    /// <summary>
    /// Agent 级配置根目录（如 skills、profile 等共享数据）
    /// </summary>
    string AgentRoot { get; }

    /// <summary>
    /// 用户级数据根目录
    /// </summary>
    string UserRoot { get; }

    /// <summary>
    /// 工作目录路径，命令行工具在此目录下执行
    /// </summary>
    string WorkingDirectory { get; }
}
