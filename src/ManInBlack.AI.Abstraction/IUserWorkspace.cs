namespace ManInBlack.AI.Abstraction;

/// <summary>
/// 用户工作空间接口，提供用户级文件系统的访问能力
/// </summary>
public interface IUserWorkspace
{
    /// <summary>
    /// 工作目录路径，命令行工具在此目录下执行
    /// </summary>
    string WorkingDirectory { get; }
}
